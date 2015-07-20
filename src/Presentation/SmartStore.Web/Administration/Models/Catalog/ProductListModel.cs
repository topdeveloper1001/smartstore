﻿﻿using System.Collections.Generic;
using System.Web.Mvc;
using SmartStore.Web.Framework;
using SmartStore.Web.Framework.Mvc;
using Telerik.Web.Mvc;

namespace SmartStore.Admin.Models.Catalog
{
    public partial class ProductListModel : ModelBase
    {
        public ProductListModel()
        {
            AvailableCategories = new List<SelectListItem>();
            AvailableManufacturers = new List<SelectListItem>();
			AvailableStores = new List<SelectListItem>();
			AvailableProductTypes = new List<SelectListItem>();
			AvailableIsPublished = new List<SelectListItem>();
			AvailableHomePageProducts = new List<SelectListItem>();
			AvailableWithoutCategories = new List<SelectListItem>();
			AvailableWithoutManufacturers = new List<SelectListItem>();
        }

        public GridModel<ProductModel> Products { get; set; }

        [SmartResourceDisplayName("Admin.Catalog.Products.List.SearchProductName")]
        [AllowHtml]
        public string SearchProductName { get; set; }

        [SmartResourceDisplayName("Admin.Catalog.Products.List.SearchCategory")]
        public int SearchCategoryId { get; set; }

		[SmartResourceDisplayName("Admin.Catalog.Products.List.SearchWithoutCategories")]
		public bool? SearchWithoutCategories { get; set; }

        [SmartResourceDisplayName("Admin.Catalog.Products.List.SearchManufacturer")]
        public int SearchManufacturerId { get; set; }

		[SmartResourceDisplayName("Admin.Catalog.Products.List.SearchWithoutManufacturers")]
		public bool? SearchWithoutManufacturers { get; set; }

		[SmartResourceDisplayName("Admin.Common.Store.SearchFor")]
		public int SearchStoreId { get; set; }

		[SmartResourceDisplayName("Admin.Catalog.Products.List.SearchProductType")]
		public int SearchProductTypeId { get; set; }

		[SmartResourceDisplayName("Admin.Catalog.Products.List.SearchIsPublished")]
		public bool? SearchIsPublished { get; set; }

		[SmartResourceDisplayName("Admin.Catalog.Products.List.SearchHomePageProducts")]
		public bool? SearchHomePageProducts { get; set; }

        [SmartResourceDisplayName("Admin.Catalog.Products.List.GoDirectlyToSku")]
        [AllowHtml]
        public string GoDirectlyToSku { get; set; }

        public bool DisplayProductPictures { get; set; }
        public bool DisplayPdfExport { get; set; }
		public int GridPageSize { get; set; }
		public int StoreCount { get; set; }

        public IList<SelectListItem> AvailableCategories { get; set; }
        public IList<SelectListItem> AvailableManufacturers { get; set; }
		public IList<SelectListItem> AvailableStores { get; set; }
		public IList<SelectListItem> AvailableWithoutCategories { get; set; }
		public IList<SelectListItem> AvailableWithoutManufacturers { get; set; }
		public IList<SelectListItem> AvailableProductTypes { get; set; }
		public IList<SelectListItem> AvailableIsPublished { get; set; }
		public IList<SelectListItem> AvailableHomePageProducts { get; set; }
    }
}