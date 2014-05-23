using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using SmartStore.Core;
using SmartStore.Core.Caching;
using SmartStore.Core.Data;
using SmartStore.Core.Domain.Catalog;
using SmartStore.Core.Domain.Security;
using SmartStore.Core.Domain.Stores;
using SmartStore.Core.Events;
using SmartStore.Services.Localization;
using SmartStore.Services.Security;
using SmartStore.Services.Stores;

namespace SmartStore.Services.Catalog
{
    /// <summary>
    /// Category service
    /// </summary>
    public partial class CategoryService : ICategoryService
    {
        #region Constants
        private const string CATEGORIES_BY_PARENT_CATEGORY_ID_KEY = "SmartStore.category.byparent-{0}-{1}-{2}-{3}";
		private const string PRODUCTCATEGORIES_ALLBYCATEGORYID_KEY = "SmartStore.productcategory.allbycategoryid-{0}-{1}-{2}-{3}-{4}-{5}";
		private const string PRODUCTCATEGORIES_ALLBYPRODUCTID_KEY = "SmartStore.productcategory.allbyproductid-{0}-{1}-{2}-{3}";
        private const string CATEGORIES_PATTERN_KEY = "SmartStore.category.";
        private const string PRODUCTCATEGORIES_PATTERN_KEY = "SmartStore.productcategory.";
        private const string CATEGORIES_BY_ID_KEY = "SmartStore.category.id-{0}";

        #endregion

        #region Fields

        private readonly IRepository<Category> _categoryRepository;
        private readonly IRepository<ProductCategory> _productCategoryRepository;
        private readonly IRepository<Product> _productRepository;
        private readonly IRepository<AclRecord> _aclRepository;
		private readonly IRepository<StoreMapping> _storeMappingRepository;
        private readonly IWorkContext _workContext;
		private readonly IStoreContext _storeContext;
        private readonly IEventPublisher _eventPublisher;
        private readonly ICacheManager _cacheManager;
		private readonly IStoreMappingService _storeMappingService;
		private readonly IAclService _aclService;
        private readonly Lazy<IEnumerable<ICategoryNavigationFilter>> _navigationFilters;

        #endregion
        
        #region Ctor

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="cacheManager">Cache manager</param>
        /// <param name="categoryRepository">Category repository</param>
        /// <param name="productCategoryRepository">ProductCategory repository</param>
        /// <param name="productRepository">Product repository</param>
        /// <param name="aclRepository">ACL record repository</param>
		/// <param name="storeMappingRepository">Store mapping repository</param>
        /// <param name="workContext">Work context</param>
		/// <param name="storeContext">Store context</param>
        /// <param name="eventPublisher">Event publisher</param>
        public CategoryService(ICacheManager cacheManager,
            IRepository<Category> categoryRepository,
            IRepository<ProductCategory> productCategoryRepository,
            IRepository<Product> productRepository,
            IRepository<AclRecord> aclRepository,
			IRepository<StoreMapping> storeMappingRepository,
            IWorkContext workContext,
			IStoreContext storeContext,
            IEventPublisher eventPublisher,
			IStoreMappingService storeMappingService,
			IAclService aclService,
            Lazy<IEnumerable<ICategoryNavigationFilter>> navigationFilters)
        {
            this._cacheManager = cacheManager;
            this._categoryRepository = categoryRepository;
            this._productCategoryRepository = productCategoryRepository;
            this._productRepository = productRepository;
            this._aclRepository = aclRepository;
			this._storeMappingRepository = storeMappingRepository;
            this._workContext = workContext;
			this._storeContext = storeContext;
            this._eventPublisher = eventPublisher;
			this._storeMappingService = storeMappingService;
			this._aclService = aclService;
            this._navigationFilters = navigationFilters;

			this.QuerySettings = DbQuerySettings.Default;
        }

		public DbQuerySettings QuerySettings { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Delete category
        /// </summary>
        /// <param name="category">Category</param>
        public virtual void DeleteCategory(Category category)
        {
            if (category == null)
                throw new ArgumentNullException("category");

            category.Deleted = true;
            UpdateCategory(category);

			//reset a "Parent category" property of all child subcategories
            var subcategories = GetAllCategoriesByParentCategoryId(category.Id, true);
            foreach (var subcategory in subcategories)
            {
                subcategory.ParentCategoryId = 0;
                UpdateCategory(subcategory);
            }
        }
        
        /// <summary>
        /// Gets all categories
        /// </summary>
        /// <param name="categoryName">Category name</param>
        /// <param name="pageIndex">Page index</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="showHidden">A value indicating whether to show hidden records</param>
		/// <param name="alias">Alias to be filtered</param>
        /// <param name="applyNavigationFilters">Whether to apply <see cref="ICategoryNavigationFilter"/> instances to the actual categories query. Never applied when <paramref name="showHidden"/> is <c>true</c></param>
        /// <returns>Categories</returns>
		/// <remarks>codehint: sm-edit</remarks>
        public virtual IPagedList<Category> GetAllCategories(string categoryName = "", int pageIndex = 0, int pageSize = int.MaxValue, bool showHidden = false, string alias = null, bool applyNavigationFilters = true)
        {
            var query = _categoryRepository.Table;
            if (!showHidden)
                query = query.Where(c => c.Published);
            if (!String.IsNullOrWhiteSpace(categoryName))
                query = query.Where(c => c.Name.Contains(categoryName));
			if (!String.IsNullOrWhiteSpace(alias))
				query = query.Where(c => c.Alias.Contains(alias));
            query = query.Where(c => !c.Deleted);
            query = query.OrderBy(c => c.ParentCategoryId).ThenBy(c => c.DisplayOrder);
            
            if (!showHidden)
            {
                query = ApplyHiddenCategoriesFilter(query, applyNavigationFilters);
				query = query.OrderBy(c => c.ParentCategoryId).ThenBy(c => c.DisplayOrder);
            }

            var unsortedCategories = query.ToList();

            // sort categories
            var sortedCategories = unsortedCategories.SortCategoriesForTree(ignoreCategoriesWithoutExistingParent: true);

            // paging
            return new PagedList<Category>(sortedCategories, pageIndex, pageSize);
        }

        /// <summary>
        /// Gets all categories filtered by parent category identifier
        /// </summary>
        /// <param name="parentCategoryId">Parent category identifier</param>
        /// <param name="showHidden">A value indicating whether to show hidden records</param>
        /// <returns>Category collection</returns>
        public IList<Category> GetAllCategoriesByParentCategoryId(int parentCategoryId, bool showHidden = false)
        {
			string key = string.Format(CATEGORIES_BY_PARENT_CATEGORY_ID_KEY, parentCategoryId, showHidden, _workContext.CurrentCustomer.Id, _storeContext.CurrentStore.Id);
            return _cacheManager.Get(key, () =>
            {
                var query = _categoryRepository.Table;
                if (!showHidden)
                    query = query.Where(c => c.Published);
                query = query.Where(c => c.ParentCategoryId == parentCategoryId);
                query = query.Where(c => !c.Deleted);
                query = query.OrderBy(c => c.DisplayOrder);

                if (!showHidden)
                {
                    query = ApplyHiddenCategoriesFilter(query, false);
					query = query.OrderBy(c => c.DisplayOrder);
                }

                var categories = query.ToList();
                return categories;
            });

        }

        protected virtual IQueryable<Category> ApplyHiddenCategoriesFilter(IQueryable<Category> query, bool applyNavigationFilters)
        {
            // ACL (access control list)
            var allowedCustomerRolesIds = _workContext.CurrentCustomer.CustomerRoles.Where(x => x.Active).Select(x => x.Id).ToList();

			if (!QuerySettings.IgnoreAcl)
			{
				query = from c in query
						join acl in _aclRepository.Table
						on new { c1 = c.Id, c2 = "Category" } equals new { c1 = acl.EntityId, c2 = acl.EntityName } into c_acl
						from acl in c_acl.DefaultIfEmpty()
						where !c.SubjectToAcl || allowedCustomerRolesIds.Contains(acl.CustomerRoleId)
						select c;
			}

			if (!QuerySettings.IgnoreMultiStore)
			{
				//Store mapping
				var currentStoreId = _storeContext.CurrentStore.Id;
				query = from c in query
						join sm in _storeMappingRepository.Table
						on new { c1 = c.Id, c2 = "Category" } equals new { c1 = sm.EntityId, c2 = sm.EntityName } into c_sm
						from sm in c_sm.DefaultIfEmpty()
						where !c.LimitedToStores || currentStoreId == sm.StoreId
						select c;
			}

            //only distinct categories (group by ID)
            query = from c in query
                    group c by c.Id into cGroup
                    orderby cGroup.Key
                    select cGroup.FirstOrDefault();

            if (applyNavigationFilters)
            {
                var filters = _navigationFilters.Value;
                if (filters.Any())
                {
                    filters.Each(x => {
                        query = x.Apply(query);
                    });
                }
            }

			return query;
        }
        
        /// <summary>
        /// Gets all categories displayed on the home page
        /// </summary>
        /// <returns>Categories</returns>
        public virtual IList<Category> GetAllCategoriesDisplayedOnHomePage()
        {
            var query = from c in _categoryRepository.Table
                        orderby c.DisplayOrder
                        where c.Published &&
                        !c.Deleted && 
                        c.ShowOnHomePage
                        select c;

            var categories = query.ToList();
            return categories;
        }
                
        /// <summary>
        /// Gets a category
        /// </summary>
        /// <param name="categoryId">Category identifier</param>
        /// <returns>Category</returns>
        public virtual Category GetCategoryById(int categoryId)
        {
            if (categoryId == 0)
                return null;

            string key = string.Format(CATEGORIES_BY_ID_KEY, categoryId);
            return _cacheManager.Get(key, () =>
            {
                return  _categoryRepository.GetById(categoryId);
            });
        }

        /// <summary>
        /// Inserts category
        /// </summary>
        /// <param name="category">Category</param>
        public virtual void InsertCategory(Category category)
        {
            if (category == null)
                throw new ArgumentNullException("category");

            _categoryRepository.Insert(category);

            //cache
            _cacheManager.RemoveByPattern(CATEGORIES_PATTERN_KEY);
            _cacheManager.RemoveByPattern(PRODUCTCATEGORIES_PATTERN_KEY);

            //event notification
            _eventPublisher.EntityInserted(category);
        }

        /// <summary>
        /// Updates the category
        /// </summary>
        /// <param name="category">Category</param>
        public virtual void UpdateCategory(Category category)
        {
            if (category == null)
                throw new ArgumentNullException("category");

            //validate category hierarchy
            var parentCategory = GetCategoryById(category.ParentCategoryId);
            while (parentCategory != null)
            {
                if (category.Id == parentCategory.Id)
                {
                    category.ParentCategoryId = 0;
                    break;
                }
                parentCategory = GetCategoryById(parentCategory.ParentCategoryId);
            }

            _categoryRepository.Update(category);

            //cache
            _cacheManager.RemoveByPattern(CATEGORIES_PATTERN_KEY);
            _cacheManager.RemoveByPattern(PRODUCTCATEGORIES_PATTERN_KEY);

            //event notification
            _eventPublisher.EntityUpdated(category);
        }
        
        /// <summary>
        /// Update HasDiscountsApplied property (used for performance optimization)
        /// </summary>
        /// <param name="category">Category</param>
        public virtual void UpdateHasDiscountsApplied(Category category)
        {
            if (category == null)
                throw new ArgumentNullException("category");

            category.HasDiscountsApplied = category.AppliedDiscounts.Count > 0;
            UpdateCategory(category);
        }

        /// <summary>
        /// Deletes a product category mapping
        /// </summary>
        /// <param name="productCategory">Product category</param>
        public virtual void DeleteProductCategory(ProductCategory productCategory)
        {
            if (productCategory == null)
                throw new ArgumentNullException("productCategory");

            _productCategoryRepository.Delete(productCategory);

            //cache
            _cacheManager.RemoveByPattern(CATEGORIES_PATTERN_KEY);
            _cacheManager.RemoveByPattern(PRODUCTCATEGORIES_PATTERN_KEY);

            //event notification
            _eventPublisher.EntityDeleted(productCategory);
        }

        /// <summary>
        /// Gets product category mapping collection
        /// </summary>
        /// <param name="categoryId">Category identifier</param>
        /// <param name="pageIndex">Page index</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="showHidden">A value indicating whether to show hidden records</param>
        /// <returns>Product a category mapping collection</returns>
        public virtual IPagedList<ProductCategory> GetProductCategoriesByCategoryId(int categoryId, int pageIndex, int pageSize, bool showHidden = false)
        {
            if (categoryId == 0)
                return new PagedList<ProductCategory>(new List<ProductCategory>(), pageIndex, pageSize);

			string key = string.Format(PRODUCTCATEGORIES_ALLBYCATEGORYID_KEY, showHidden, categoryId, pageIndex, pageSize, _workContext.CurrentCustomer.Id, _storeContext.CurrentStore.Id);
            return _cacheManager.Get(key, () =>
            {
                var query = from pc in _productCategoryRepository.Table
                            join p in _productRepository.Table on pc.ProductId equals p.Id
                            where pc.CategoryId == categoryId &&
                                  !p.Deleted &&
                                  (showHidden || p.Published)
                            orderby pc.DisplayOrder
                            select pc;

                if (!showHidden)
                {
                    query = ApplyHiddenProductCategoriesFilter(query);
					query = query.OrderBy(pc => pc.DisplayOrder);
                }

                var productCategories = new PagedList<ProductCategory>(query, pageIndex, pageSize);
                return productCategories;
            });
        }

        /// <summary>
        /// Gets a product category mapping collection
        /// </summary>
        /// <param name="productId">Product identifier</param>
        /// <param name="showHidden">A value indicating whether to show hidden records</param>
        /// <returns>Product category mapping collection</returns>
        public virtual IList<ProductCategory> GetProductCategoriesByProductId(int productId, bool showHidden = false)
        {
            if (productId == 0)
                return new List<ProductCategory>();

			string key = string.Format(PRODUCTCATEGORIES_ALLBYPRODUCTID_KEY, showHidden, productId, _workContext.CurrentCustomer.Id, _storeContext.CurrentStore.Id);
            return _cacheManager.Get(key, () =>
            {
				var table = _productCategoryRepository.Table;
				var query = from pc in _productCategoryRepository.Expand(table, x => x.Category)
                            join c in _categoryRepository.Table on pc.CategoryId equals c.Id
                            where pc.ProductId == productId &&
                                  !c.Deleted &&
                                  (showHidden || c.Published)
                            orderby pc.DisplayOrder
                            select pc;

				var allProductCategories = query.ToList();
				var result = new List<ProductCategory>();
				if (!showHidden)
				{
					foreach (var pc in allProductCategories)
					{
						// ACL (access control list) and store mapping
						var category = pc.Category;
						if (_aclService.Authorize(category) && _storeMappingService.Authorize(category))
							result.Add(pc);
					}
				}
				else
				{
					// No filtering
					result.AddRange(allProductCategories);
				}
				return result;
            });
        }

		protected virtual IQueryable<ProductCategory> ApplyHiddenProductCategoriesFilter(IQueryable<ProductCategory> query)
        {
            //ACL (access control list)
            var allowedCustomerRolesIds = _workContext.CurrentCustomer.CustomerRoles
                .Where(cr => cr.Active).Select(cr => cr.Id).ToList();

            query = from pc in query
					join c in _categoryRepository.Table on pc.CategoryId equals c.Id
					join acl in _aclRepository.Table
					on new { c1 = c.Id, c2 = "Category" } equals new { c1 = acl.EntityId, c2 = acl.EntityName } into c_acl
					from acl in c_acl.DefaultIfEmpty()
					where !c.SubjectToAcl || allowedCustomerRolesIds.Contains(acl.CustomerRoleId)
                    select pc;

            //Store mapping
            var currentStoreId = _storeContext.CurrentStore.Id;
            query = from pc in query
					join c in _categoryRepository.Table on pc.CategoryId equals c.Id
					join sm in _storeMappingRepository.Table
					on new { c1 = c.Id, c2 = "Category" } equals new { c1 = sm.EntityId, c2 = sm.EntityName } into c_sm
					from sm in c_sm.DefaultIfEmpty()
					where !c.LimitedToStores || currentStoreId == sm.StoreId
                    select pc;

            //only distinct categories (group by ID)
            query = from pc in query
                    group pc by pc.Id into pcGroup
                    orderby pcGroup.Key
                    select pcGroup.FirstOrDefault();

			return query;
        }

        /// <summary>
        /// Gets a product category mapping 
        /// </summary>
        /// <param name="productCategoryId">Product category mapping identifier</param>
        /// <returns>Product category mapping</returns>
        public virtual ProductCategory GetProductCategoryById(int productCategoryId)
        {
            if (productCategoryId == 0)
                return null;

            return _productCategoryRepository.GetById(productCategoryId);
        }

        /// <summary>
        /// Inserts a product category mapping
        /// </summary>
        /// <param name="productCategory">>Product category mapping</param>
        public virtual void InsertProductCategory(ProductCategory productCategory)
        {
            if (productCategory == null)
                throw new ArgumentNullException("productCategory");
            
            _productCategoryRepository.Insert(productCategory);

            //cache
            _cacheManager.RemoveByPattern(CATEGORIES_PATTERN_KEY);
            _cacheManager.RemoveByPattern(PRODUCTCATEGORIES_PATTERN_KEY);

            //event notification
            _eventPublisher.EntityInserted(productCategory);
        }

        /// <summary>
        /// Updates the product category mapping 
        /// </summary>
        /// <param name="productCategory">>Product category mapping</param>
        public virtual void UpdateProductCategory(ProductCategory productCategory)
        {
            if (productCategory == null)
                throw new ArgumentNullException("productCategory");

            _productCategoryRepository.Update(productCategory);

            //cache
            _cacheManager.RemoveByPattern(CATEGORIES_PATTERN_KEY);
            _cacheManager.RemoveByPattern(PRODUCTCATEGORIES_PATTERN_KEY);

            //event notification
            _eventPublisher.EntityUpdated(productCategory);
        }

		public virtual string GetCategoryPath(Product product, int? languageId, Func<int, string> pathLookup, Action<int, string> addPathToCache, Func<int, Category> categoryLookup)
		{
			if (product == null)
				return string.Empty;

			pathLookup = pathLookup ?? ((i) => { return string.Empty; });
			categoryLookup = categoryLookup ?? ((i) => { return GetCategoryById(i); });
			addPathToCache = addPathToCache ?? ((i, val) => { });

			var alreadyProcessedCategoryIds = new List<int>();
			var path = new List<string>();

			var productCategory = GetProductCategoriesByProductId(product.Id).FirstOrDefault();

			if (productCategory != null && productCategory.Category != null)
			{
				string cached = pathLookup(productCategory.CategoryId);
				if (cached.HasValue()) 
				{
					return cached;
				}

				var category = productCategory.Category;

				path.Add(languageId.HasValue ? category.GetLocalized(x => x.Name, languageId.Value) : category.Name);
				alreadyProcessedCategoryIds.Add(category.Id);

				category = categoryLookup(category.ParentCategoryId);
				while (category != null && !category.Deleted && category.Published && !alreadyProcessedCategoryIds.Contains(category.Id))
				{
					path.Add(languageId.HasValue ? category.GetLocalized(x => x.Name, languageId.Value) : category.Name);
					alreadyProcessedCategoryIds.Add(category.Id);
					category = categoryLookup(category.ParentCategoryId);
				}

				path.Reverse();
				string result = String.Join(" > ", path);
				addPathToCache(productCategory.CategoryId, result);
				return result;
			}

			return string.Empty;
		}

        #endregion
    }
}
