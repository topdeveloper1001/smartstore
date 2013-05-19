﻿using System;
using System.Collections.Generic;
using System.Web.Mvc;
using SmartStore.Web.Framework.Mvc;
using SmartStore.Web.Models.Media;

namespace SmartStore.Web.Models.ShoppingCart
{
    public partial class WishlistModel : ModelBase
    {
        public WishlistModel()
        {
            Items = new List<ShoppingCartItemModel>();
            Warnings = new List<string>();
        }

        public Guid CustomerGuid { get; set; }
        public string CustomerFullname { get; set; }

        public bool EmailWishlistEnabled { get; set; }

        public bool ShowSku { get; set; }

        public bool ShowProductImages { get; set; }

        public bool IsEditable { get; set; }

        public bool DisplayAddToCart { get; set; }

        public IList<ShoppingCartItemModel> Items { get; set; }

        public IList<string> Warnings { get; set; }

        // codehint: sm-add
        public int ThumbSize { get; set; }
        public int IgnoredProductsCount { get; set; }
        
		#region Nested Classes

        public partial class ShoppingCartItemModel : EntityModelBase
        {
            public ShoppingCartItemModel()
            {
                Picture = new PictureModel();
                AllowedQuantities = new List<SelectListItem>();
                Warnings = new List<string>();
            }
            public string Sku { get; set; }

            public PictureModel Picture {get;set;}

            public int ProductId { get; set; }

            public string ProductName { get; set; }

            public string ProductSeName { get; set; }

            public string UnitPrice { get; set; }

            public string SubTotal { get; set; }

            public string Discount { get; set; }

            public int Quantity { get; set; }
            public List<SelectListItem> AllowedQuantities { get; set; }
            
            public string AttributeInfo { get; set; }

            public string RecurringInfo { get; set; }

            public IList<string> Warnings { get; set; }

        }

		#endregion
    }
}