using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.StaticFiles;
using Nop.Core;
using Nop.Core.Domain.Blogs;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Cms;
using Nop.Core.Domain.Menus;
using Nop.Core.Domain.News;
using Nop.Core.Domain.Seo;
using Nop.Core.Domain.Topics;
using Nop.Core.Infrastructure;
using Nop.Services.Blogs;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Menus;
using Nop.Services.Seo;
using Nop.Services.Stores;
using Nop.Services.Topics;

namespace Nop.Plugin.Misc.AlThamin.Services;

/// <summary>
/// Imports the althamin.ae content snapshot (Content/althamin-seed.json) into nopCommerce.
/// Safe to run again: entities are matched by slug (or topic system name) and updated in place.
/// </summary>
public partial class ContentImporter
{
    #region Seed model

    public class SeedPage
    {
        public string LegacyPath { get; set; }
        public string Slug { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string MetaTitle { get; set; }
        public string MetaDescription { get; set; }
        public string Picture { get; set; }
        public string Body { get; set; }
        public string Parent { get; set; }
        public string Category { get; set; }
        public string ShortDescription { get; set; }
        public string SystemName { get; set; }
        public int Order { get; set; }
        public bool ShowOnHome { get; set; }
        public bool IncludeInSitemap { get; set; }
        public List<string> Tags { get; set; } = [];
        public string CreatedOnUtc { get; set; }
    }

    public class Seed
    {
        public List<SeedPage> Categories { get; set; } = [];
        public List<SeedPage> Products { get; set; } = [];
        public List<SeedPage> Topics { get; set; } = [];
        public List<SeedPage> BlogPosts { get; set; } = [];
    }

    #endregion

    #region Fields

    protected readonly IBlogService _blogService;
    protected readonly ICategoryService _categoryService;
    protected readonly ILanguageService _languageService;
    protected readonly IMenuService _menuService;
    protected readonly INopFileProvider _fileProvider;
    protected readonly IPictureService _pictureService;
    protected readonly IProductService _productService;
    protected readonly ISettingService _settingService;
    protected readonly IStoreContext _storeContext;
    protected readonly IStoreService _storeService;
    protected readonly ITopicService _topicService;
    protected readonly IUrlRecordService _urlRecordService;

    protected readonly List<string> _log = [];

    #endregion

    #region Ctor

    public ContentImporter(IBlogService blogService,
        ICategoryService categoryService,
        ILanguageService languageService,
        IMenuService menuService,
        INopFileProvider fileProvider,
        IPictureService pictureService,
        IProductService productService,
        ISettingService settingService,
        IStoreContext storeContext,
        IStoreService storeService,
        ITopicService topicService,
        IUrlRecordService urlRecordService)
    {
        _blogService = blogService;
        _categoryService = categoryService;
        _languageService = languageService;
        _menuService = menuService;
        _fileProvider = fileProvider;
        _pictureService = pictureService;
        _productService = productService;
        _settingService = settingService;
        _storeContext = storeContext;
        _storeService = storeService;
        _topicService = topicService;
        _urlRecordService = urlRecordService;
    }

    #endregion

    #region Utilities

    protected virtual async Task<Seed> LoadSeedAsync()
    {
        var path = _fileProvider.MapPath(AlThaminDefaults.SeedFilePath);
        if (!_fileProvider.FileExists(path))
            throw new NopException($"Seed file not found: {path}");

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };
        return JsonSerializer.Deserialize<Seed>(await _fileProvider.ReadAllTextAsync(path, System.Text.Encoding.UTF8), options);
    }

    /// <summary>
    /// Find an entity of the given type by its slug
    /// </summary>
    protected virtual async Task<int> FindIdBySlugAsync(string entityName, string slug)
    {
        if (string.IsNullOrEmpty(slug))
            return 0;

        var record = await _urlRecordService.GetBySlugAsync(slug);
        return record is not null && record.EntityName.Equals(entityName, StringComparison.InvariantCultureIgnoreCase) ? record.EntityId : 0;
    }

    /// <summary>
    /// Store an image from /wwwroot/wp-content/uploads/... as a nopCommerce picture
    /// </summary>
    protected virtual async Task<int> InsertPictureAsync(string webPath, string alt)
    {
        if (string.IsNullOrEmpty(webPath))
            return 0;

        var file = _fileProvider.Combine(_fileProvider.WebRootPath, webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!_fileProvider.FileExists(file))
        {
            _log.Add($"Image missing on disk: {webPath}");
            return 0;
        }

        if (!new FileExtensionContentTypeProvider().TryGetContentType(file, out var mimeType))
            mimeType = MimeTypes.ImageJpeg;

        var seoName = await _pictureService.GetPictureSeNameAsync(_fileProvider.GetFileNameWithoutExtension(file));
        var picture = await _pictureService.InsertPictureAsync(await _fileProvider.ReadAllBytesAsync(file), mimeType, seoName, alt, alt);
        return picture.Id;
    }

    protected static DateTime ParseUtc(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.UtcDateTime
            : DateTime.UtcNow;
    }

    protected virtual async Task ApplySettingsAsync()
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        store.Name = "Al Thamin Technologies";
        store.CompanyName = "Al Thamin Technologies";
        store.CompanyAddress = "Deira Business Centre, Iridium, Deira, Dubai, UAE";
        store.CompanyPhoneNumber = "+971 52 184 7186";
        store.DefaultTitle = "Al Thamin Technologies";
        //the old homepage <title> was "Home - Al Thamin Technologies"; kept as-is to avoid ranking changes
        store.HomepageTitle = "Home";
        store.HomepageDescription = "Al Thamin Technologies supplies, installs and maintains LED walls, digital signage, kiosks and menu boards in Dubai, Sharjah and across the UAE since 2000.";
        await _storeService.UpdateStoreAsync(store);

        //"{page title} - Al Thamin Technologies", exactly like the WordPress (Yoast) titles
        var seoSettings = await _settingService.LoadSettingAsync<SeoSettings>();
        seoSettings.PageTitleSeparator = " - ";
        seoSettings.PageTitleSeoAdjustment = PageTitleSeoAdjustment.StorenameAfterPagename;
        seoSettings.CanonicalUrlsEnabled = true;
        seoSettings.QueryStringInCanonicalUrlsEnabled = false;
        seoSettings.GenerateProductMetaDescription = true;
        await _settingService.SaveSettingAsync(seoSettings);

        //lead-generation site: no shopping features in the storefront
        var catalogSettings = await _settingService.LoadSettingAsync<CatalogSettings>();
        catalogSettings.EmailAFriendEnabled = false;
        catalogSettings.CompareProductsEnabled = false;
        catalogSettings.RecentlyViewedProductsEnabled = false;
        catalogSettings.NewProductsEnabled = false;
        catalogSettings.ShowProductsFromSubcategories = false;
        catalogSettings.ShowSkuOnProductDetailsPage = false;
        catalogSettings.ShowShareButton = false;
        catalogSettings.ProductUrlStructureTypeId = (int)ProductUrlStructureType.Product;
        await _settingService.SaveSettingAsync(catalogSettings);

        var blogSettings = await _settingService.LoadSettingAsync<BlogSettings>();
        blogSettings.Enabled = true;
        blogSettings.AllowNotRegisteredUsersToLeaveComments = false;
        blogSettings.PostsPageSize = 12;
        await _settingService.SaveSettingAsync(blogSettings);

        var newsSettings = await _settingService.LoadSettingAsync<NewsSettings>();
        newsSettings.Enabled = false;
        await _settingService.SaveSettingAsync(newsSettings);

        //no demo slider / shop widgets on the storefront; keep analytics only
        var widgetSettings = await _settingService.LoadSettingAsync<WidgetSettings>();
        widgetSettings.ActiveWidgetSystemNames = widgetSettings.ActiveWidgetSystemNames
            .Where(name => name.Equals("Widgets.GoogleAnalytics", StringComparison.OrdinalIgnoreCase))
            .ToList();
        await _settingService.SaveSettingAsync(widgetSettings);

        _log.Add("Store, SEO, catalog, blog and widget settings applied.");
    }

    protected virtual async Task<Dictionary<string, Category>> ImportCategoriesAsync(Seed seed)
    {
        var bySlug = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in seed.Categories)
        {
            var category = await _categoryService.GetCategoryByIdAsync(await FindIdBySlugAsync(nameof(Category), item.Slug));
            var isNew = category is null || category.Deleted;
            category = isNew ? new Category { CreatedOnUtc = DateTime.UtcNow, CategoryTemplateId = 1, PageSize = 24, AllowCustomersToSelectPageSize = false, PageSizeOptions = "12, 24, 48" } : category;

            category.Name = item.Name;
            category.Description = item.Body;
            category.MetaTitle = item.MetaTitle;
            category.MetaDescription = item.MetaDescription;
            category.ParentCategoryId = item.Parent is not null && bySlug.TryGetValue(item.Parent, out var parent) ? parent.Id : 0;
            category.DisplayOrder = item.Order;
            category.ShowOnHomepage = item.ShowOnHome;
            category.Published = true;
            category.UpdatedOnUtc = DateTime.UtcNow;
            if (category.PictureId == 0)
                category.PictureId = await InsertPictureAsync(item.Picture, item.Name);

            if (isNew)
                await _categoryService.InsertCategoryAsync(category);
            else
                await _categoryService.UpdateCategoryAsync(category);

            await _urlRecordService.SaveSlugAsync(category, item.Slug, 0);
            bySlug[item.Slug] = category;
            _log.Add($"{(isNew ? "Created" : "Updated")} category {item.LegacyPath}");
        }

        return bySlug;
    }

    protected virtual async Task ImportProductsAsync(Seed seed, Dictionary<string, Category> categories)
    {
        foreach (var item in seed.Products)
        {
            var product = await _productService.GetProductByIdAsync(await FindIdBySlugAsync(nameof(Product), item.Slug));
            var isNew = product is null || product.Deleted;
            product = isNew ? new Product
            {
                ProductType = ProductType.SimpleProduct,
                ProductTemplateId = 1,
                VisibleIndividually = true,
                OrderMinimumQuantity = 1,
                OrderMaximumQuantity = 10000,
                CreatedOnUtc = DateTime.UtcNow
            } : product;

            product.Name = item.Name;
            product.ShortDescription = item.ShortDescription;
            product.FullDescription = item.Body;
            product.MetaTitle = item.MetaTitle;
            product.MetaDescription = item.MetaDescription;
            product.DisplayOrder = item.Order;
            product.ShowOnHomepage = item.ShowOnHome;
            product.Published = true;
            //quote-only: no prices, cart or wishlist
            product.CallForPrice = true;
            product.DisableBuyButton = true;
            product.DisableWishlistButton = true;
            product.IsShipEnabled = false;
            product.AllowCustomerReviews = false;
            product.UpdatedOnUtc = DateTime.UtcNow;

            if (isNew)
                await _productService.InsertProductAsync(product);
            else
                await _productService.UpdateProductAsync(product);

            await _urlRecordService.SaveSlugAsync(product, item.Slug, 0);

            if (categories.TryGetValue(item.Category, out var category) &&
                !(await _categoryService.GetProductCategoriesByProductIdAsync(product.Id, true)).Any(pc => pc.CategoryId == category.Id))
            {
                await _categoryService.InsertProductCategoryAsync(new ProductCategory { ProductId = product.Id, CategoryId = category.Id, DisplayOrder = item.Order });
            }

            if (!(await _productService.GetProductPicturesByProductIdAsync(product.Id)).Any())
            {
                var pictureId = await InsertPictureAsync(item.Picture, item.Name);
                if (pictureId > 0)
                    await _productService.InsertProductPictureAsync(new ProductPicture { ProductId = product.Id, PictureId = pictureId, DisplayOrder = 1 });
            }

            _log.Add($"{(isNew ? "Created" : "Updated")} product {item.LegacyPath}");
        }
    }

    protected virtual async Task<Dictionary<string, Topic>> ImportTopicsAsync(Seed seed)
    {
        var bySystemName = new Dictionary<string, Topic>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in seed.Topics)
        {
            var topic = await _topicService.GetTopicBySystemNameAsync(item.SystemName);
            var isNew = topic is null;
            topic ??= new Topic { SystemName = item.SystemName, TopicTemplateId = 1 };

            topic.Title = item.Title;
            topic.Body = item.Body;
            topic.MetaTitle = item.MetaTitle;
            topic.MetaDescription = item.MetaDescription;
            topic.IncludeInSitemap = item.IncludeInSitemap;
            topic.Published = true;

            if (isNew)
                await _topicService.InsertTopicAsync(topic);
            else
                await _topicService.UpdateTopicAsync(topic);

            if (!string.IsNullOrEmpty(item.Slug))
                await _urlRecordService.SaveSlugAsync(topic, item.Slug, 0);

            bySystemName[item.SystemName] = topic;
            _log.Add($"{(isNew ? "Created" : "Updated")} topic {item.SystemName} {item.LegacyPath}");
        }

        return bySystemName;
    }

    protected virtual async Task ImportBlogPostsAsync(Seed seed)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var languageId = store.DefaultLanguageId > 0 ? store.DefaultLanguageId : (await _languageService.GetAllLanguagesAsync()).First().Id;

        foreach (var item in seed.BlogPosts)
        {
            var post = await _blogService.GetBlogPostByIdAsync(await FindIdBySlugAsync(nameof(BlogPost), item.Slug));
            var isNew = post is null;
            post ??= new BlogPost();

            post.LanguageId = languageId;
            post.Title = item.Title;
            post.Body = item.Body;
            post.BodyOverview = item.MetaDescription;
            post.MetaTitle = item.MetaTitle;
            post.MetaDescription = item.MetaDescription;
            post.Tags = string.Join(", ", item.Tags);
            post.AllowComments = false;
            post.IncludeInSitemap = true;
            post.CreatedOnUtc = ParseUtc(item.CreatedOnUtc);

            if (isNew)
                await _blogService.InsertBlogPostAsync(post);
            else
                await _blogService.UpdateBlogPostAsync(post);

            //language 0 = the default slug, as nopCommerce admin saves it (needed for GetSeNameAsync / sitemap)
            await _urlRecordService.SaveSlugAsync(post, item.Slug, 0);
            _log.Add($"{(isNew ? "Created" : "Updated")} blog post {item.LegacyPath}");
        }
    }

    protected virtual async Task<Menu> GetOrCreateMenuAsync(MenuType type, string name, int displayOrder)
    {
        var menus = await _menuService.GetAllMenusAsync(type, showHidden: true);
        var menu = menus.FirstOrDefault(m => m.Name == name);
        if (menu is null)
        {
            menu = new Menu { Name = name, MenuTypeId = (int)type, DisplayOrder = displayOrder, Published = true };
            await _menuService.InsertMenuAsync(menu);
        }
        else if (!menu.Published)
        {
            menu.Published = true;
            await _menuService.UpdateMenuAsync(menu);
        }

        //clear the old items so a re-import does not duplicate them
        foreach (var menuItem in (await _menuService.GetAllMenuItemsAsync(menu.Id, showHidden: true)).OrderByDescending(i => i.ParentId))
            await _menuService.DeleteMenuItemAsync(menuItem);

        return menu;
    }

    protected virtual async Task<MenuItem> AddMenuItemAsync(Menu menu, int order, string title, MenuItemType type,
        string url = null, int? entityId = null, MenuItemTemplate template = MenuItemTemplate.Simple, int parentId = 0)
    {
        var item = new MenuItem
        {
            MenuId = menu.Id,
            ParentId = parentId,
            Title = title,
            MenuItemTypeId = (int)type,
            Url = url,
            EntityId = entityId,
            TemplateId = (int)template,
            MaximumNumberEntities = template == MenuItemTemplate.Simple ? null : 20,
            DisplayOrder = order,
            Published = true
        };
        await _menuService.InsertMenuItemAsync(item);
        return item;
    }

    protected virtual async Task ImportMenusAsync(Dictionary<string, Category> categories, Dictionary<string, Topic> topics)
    {
        //main menu: unpublish the others, rebuild ours
        foreach (var other in await _menuService.GetAllMenusAsync(MenuType.Main, showHidden: true))
        {
            if (other.Name == "Menu" || !other.Published)
                continue;
            other.Published = false;
            await _menuService.UpdateMenuAsync(other);
        }

        //the menu name is shown on the mobile menu button
        var main = await GetOrCreateMenuAsync(MenuType.Main, "Menu", 0);
        await AddMenuItemAsync(main, 1, "Services", MenuItemType.Category, entityId: categories[AlThaminDefaults.ServicesSlug].Id, template: MenuItemTemplate.List);
        //category / topic items always show the entity name, so short labels are custom links to the legacy URLs
        await AddMenuItemAsync(main, 2, "LED Walls", MenuItemType.CustomLink, url: "/service/led-wall-solutions/");
        await AddMenuItemAsync(main, 3, "Software", MenuItemType.CustomLink, url: "/digital-signage-software-in-dubai/");
        await AddMenuItemAsync(main, 4, "Resources", MenuItemType.CustomLink, url: "/resources/");
        await AddMenuItemAsync(main, 5, "About", MenuItemType.CustomLink, url: "/about/");
        await AddMenuItemAsync(main, 6, "Contact", MenuItemType.CustomLink, url: "/contact/");

        //footer menus
        foreach (var other in await _menuService.GetAllMenusAsync(MenuType.Footer, showHidden: true))
        {
            if (other.Name is "Services" or "Solutions" or "Company" || !other.Published)
                continue;
            other.Published = false;
            await _menuService.UpdateMenuAsync(other);
        }

        var services = await GetOrCreateMenuAsync(MenuType.Footer, "Services", 1);
        var order = 0;
        foreach (var slug in new[] { "led-wall-solutions", "digital-kiosks-uae", "digital-menu-solutions-dubai", "hotel-digital-signage-solutions-in-uae", "interactive-whiteboard-touch-board-supplier-in-dubai-uae" })
            await AddMenuItemAsync(services, ++order, null, MenuItemType.Category, entityId: categories[slug].Id);

        var solutions = await GetOrCreateMenuAsync(MenuType.Footer, "Solutions", 2);
        order = 0;
        foreach (var slug in new[] { "elevator-lobby-screens-installation-in-uae", "campus-digital-signage-solutions-in-dubai", "office-communication-display-screens-in-dubai-uae" })
            await AddMenuItemAsync(solutions, ++order, null, MenuItemType.Category, entityId: categories[slug].Id);
        foreach (var systemName in new[] { "GymPromotionScreens", "MeetingRoomDisplay", "DigitalSignageSoftware" })
            await AddMenuItemAsync(solutions, ++order, null, MenuItemType.TopicPage, entityId: topics[systemName].Id);

        var company = await GetOrCreateMenuAsync(MenuType.Footer, "Company", 3);
        await AddMenuItemAsync(company, 1, "About Al Thamin", MenuItemType.TopicPage, entityId: topics["AboutUs"].Id);
        await AddMenuItemAsync(company, 2, "Resources", MenuItemType.CustomLink, url: "/resources/");
        await AddMenuItemAsync(company, 3, "Blog", MenuItemType.CustomLink, url: "/category/blog/");
        await AddMenuItemAsync(company, 4, "Case studies", MenuItemType.CustomLink, url: "/category/case-study/");
        await AddMenuItemAsync(company, 5, "Contact", MenuItemType.CustomLink, url: "/contact/");

        _log.Add("Main and footer menus rebuilt with legacy URLs.");
    }

    #endregion

    #region Methods

    /// <summary>
    /// Import everything from the seed file
    /// </summary>
    /// <returns>Lines describing what was done</returns>
    public virtual async Task<IList<string>> ImportAsync()
    {
        _log.Clear();
        var seed = await LoadSeedAsync();

        await ApplySettingsAsync();
        var categories = await ImportCategoriesAsync(seed);
        await ImportProductsAsync(seed, categories);
        var topics = await ImportTopicsAsync(seed);
        await ImportBlogPostsAsync(seed);
        await ImportMenusAsync(categories, topics);

        return _log;
    }

    #endregion
}
