﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using blog.Data;
using blog.Models.ViewModels.Home;
using blog.Models;
using CommonMark;
using Microsoft.EntityFrameworkCore;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace blog.Controllers
{
  public class HomeController : Controller
  {
    BlogDbContext _context;

    public HomeController(BlogDbContext context)
    {
      _context = context;
    }

    public async Task<IActionResult> Index()
    {
      var recentPosts = await _context.BlogPosts
                                      .AsNoTracking()
                                      .Where(bp => bp.Public == true && bp.PublishOn < DateTime.Now)
                                      .OrderByDescending(bp => bp.ModifiedAt)
                                      .ThenByDescending(bp => bp.PublishOn)
                                      .Take(7)
                                      .ToListAsync();

      var vhvm = new ViewHomeViewModel()
      {
        RecentPosts = recentPosts
      };

      return View(vhvm);
    }

    public IActionResult RecentPosts()
    {
      return View();
    }

    public async Task<IActionResult> Search(string searchText)
    {
      var search = "\"*" + searchText + "*\"";

      var posts = await _context.BlogPosts
                          .AsNoTracking()
                          .FromSql("SELECT * from [dbo].[BlogPost] WHERE Contains((Content, Description, Title), {0})", search)
                          .Where(bp => bp.Public == true && bp.PublishOn < DateTime.Now)
                          .OrderByDescending(bp => bp.ModifiedAt)
                          .ThenByDescending(bp => bp.PublishOn)
                          .Include(bp => bp.Author)
                          .Include(bp => bp.BlogPostCategory)
                            .ThenInclude(bpc => bpc.Category)
                          .ToListAsync();

      var bps = new List<ViewSearchResultBlogPostViewModel>();

      foreach (var post in posts)
      {

        var categories = post.BlogPostCategory.Select(c => c.Category).ToList();

        var vsrbpvm = new ViewSearchResultBlogPostViewModel()
        {
          BlogPost = post,
          Categories = categories
        };

        bps.Add(vsrbpvm);

      }

      var vsrvm = new ViewSearchResultsViewModel()
      {
        SearchTerm = searchText,
        BlogPosts = bps
      };

      return View(vsrvm);
    }

    public async Task<IActionResult> CategoryPosts(long? id)
    {
      var category = await _context.Categories.FindAsync(id);

      var vcpvm = new ViewCategoryPostsViewModel()
      {
        Category = category,
        Posts = new List<BlogPost>()
      };

      if (category != null)
      {
        var bpcs = await _context.BlogPostCategory
                                .AsNoTracking()
                                .Where(bp => bp.CategoryId == category.Id && bp.BlogPost.Public == true && bp.BlogPost.PublishOn < DateTime.Now)
                                .Include(bpc => bpc.BlogPost)
                                  .ThenInclude(bp => bp.Author)
                                .ToListAsync();

        vcpvm.Category = category;
        vcpvm.Posts = bpcs.Select(bp => bp.BlogPost).ToList();
      }

      return View(vcpvm);
    }

    public async Task<IActionResult> AllPosts(int? sortby = 1)
    {
      var vm = new ViewAllPostsViewModel();

      // Sort by Category
      if (sortby == 1)
      {
        var categories = await _context.Categories.AsNoTracking().ToListAsync();

        var viewCategories = new List<ViewPostsByCategory>();

        foreach (var c in categories)
        {
          var bpcs = await _context.BlogPostCategory
                                  .AsNoTracking()
                                  .Where(bp => bp.CategoryId == c.Id && bp.BlogPost.Public == true && bp.BlogPost.PublishOn < DateTime.Now)
                                  .Include(bpc => bpc.BlogPost)
                                    .ThenInclude(bp => bp.Author)
                                  .ToListAsync();

          var vpbc = new ViewPostsByCategory()
          {
            Category = c,
            Posts = bpcs.Select(bp => bp.BlogPost).ToList()
          };

          viewCategories.Add(vpbc);
        }

        vm = new ViewAllPostsViewModel()
        {
          PostsByDate = new List<BlogPost>(),
          SortBy = sortby ?? 1,
          Categories = viewCategories ?? new List<ViewPostsByCategory>()
        };

      }
      // return sorted by date
      else
      {
        vm = new ViewAllPostsViewModel()
        {
          PostsByDate = await _context.BlogPosts
                                      .AsNoTracking()
                                      .Where(bp => bp.Public == true && bp.PublishOn < DateTime.Now)
                                      .Include(bp => bp.Author)
                                      .OrderByDescending(bp => bp.ModifiedAt)
                                      .ThenByDescending(bp => bp.PublishOn)
                                      .ToListAsync(),
          SortBy = sortby ?? 2,
          Categories = null
        };
      }
      return View(vm);
    }

    public async Task<IActionResult> FeaturePosts(long id)
    {
      var feature = await _context.Features.FindAsync(id);

      if (feature == null)
      {
        return NotFound();
      }

      var bpfs = await _context.BlogPostFeature
                              .AsNoTracking()
                              .Where(bpf => bpf.FeatureId == feature.Id && bpf.BlogPost.Public == true && bpf.BlogPost.PublishOn < DateTime.Now)
                              .Include(bpf => bpf.BlogPost)
                                .ThenInclude(bp => bp.Author)
                              .Include(bpf => bpf.BlogPost)
                                .ThenInclude(bp => bp.BlogPostCategory)
                                .ThenInclude(bpc => bpc.Category)
                              .ToListAsync();

      List<ViewBlogPostViewModel> viewPosts = new List<ViewBlogPostViewModel>();

      foreach (var bpf in bpfs)
      {
        var bpView = new ViewBlogPostViewModel()
        {
          Author = $"{bpf.BlogPost.Author.FirstName} {bpf.BlogPost.Author.LastName}",
          Title = bpf.BlogPost.Title,
          Description = bpf.BlogPost.Description,
          Categories = bpf.BlogPost.BlogPostCategory.Select(c => c.Category).ToList(),
          ModifiedAt = bpf.BlogPost.ModifiedAt,
          Id = bpf.BlogPost.Id
        };
        viewPosts.Add(bpView);
      }

      ViewFeaturePostsViewModel vfpvm = new ViewFeaturePostsViewModel()
      {
        Feature = feature,
        BlogPosts = viewPosts
      };

      return View(vfpvm);
    }

    public async Task<IActionResult> Features()
    {
      // bug in EF Core that needs a bit more code here than otherwise
      // see: https://github.com/aspnet/EntityFramework/issues/7714

      ViewFeaturesViewModel vfvm = new ViewFeaturesViewModel();

      // cant select new into a defined type so have to use anon type for the select new here due to EF Core bug
      // code after is a work-around

      var x = from feature in _context.Features.AsNoTracking()
              select new
              {
                Title = feature.Title,
                Description = feature.Description,
                Id = feature.Id,
                BlogCount = (from posts in _context.BlogPostFeature
                             where posts.FeatureId == feature.Id && posts.BlogPost.Public == true && posts.BlogPost.PublishOn < DateTime.Now
                             select posts).AsNoTracking().Count()
              };

      List<FeatureViewModel> featureList = new List<FeatureViewModel>();

      foreach (var feature in await x.OrderByDescending(f => f.BlogCount).ToListAsync())
      {
        FeatureViewModel fvm = new FeatureViewModel()
        {
          BlogCount = feature.BlogCount,
          Title = feature.Title,
          Description = feature.Description,
          Id = feature.Id
        };
        featureList.Add(fvm);
      }

      vfvm.Features = featureList;

      return View(vfvm);
    }

    public IActionResult About()
    {
      return View();
    }

    public async Task<IActionResult> BlogPost(long? id)
    {
      if (id == null)
        return NotFound();

      var post = await _context.BlogPosts
                          .AsNoTracking()
                          .Include(bp => bp.Author)
                          .Include(bp => bp.BlogPostCategory)
                            .ThenInclude(bpc => bpc.Category)
                          .Where(bp => bp.Id == id && bp.Public == true && bp.PublishOn < DateTime.Now)
                          .FirstOrDefaultAsync();

      if (post == null)
        return NotFound();

      var bpvm = new ViewBlogPostViewModel()
      {
        Author = $"{post.Author.FirstName} {post.Author.LastName}",
        Title = post.Title,
        Description = post.Description,
        Content = post.Content,
        ModifiedAt = post.ModifiedAt,
        Categories = post.BlogPostCategory.Select(c => c.Category).ToList()
      };

      bpvm.Content = CommonMarkConverter.Convert(bpvm.Content);

      return View(bpvm);
    }

  }

}
