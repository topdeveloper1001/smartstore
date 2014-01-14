﻿using SmartStore.Core.Domain.Catalog;
using SmartStore.Services.Catalog;
using SmartStore.Web.Framework.WebApi;
using SmartStore.Web.Framework.WebApi.OData;
using SmartStore.Web.Framework.WebApi.Security;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using SmartStore.Core.Domain.Discounts;
using SmartStore.Core.Domain.Directory;

namespace SmartStore.Plugin.Api.WebApi.Controllers.OData
{
	[WebApiAuthenticate(Permission = "ManageCatalog")]
	public class ProductsController : WebApiEntityController<Product, IProductService>
	{
		protected override IQueryable<Product> GetEntitySet()
		{
			var query =
				from x in this.Repository.Table
				where !x.Deleted
				select x;

			return query;
		}
		protected override void Insert(Product entity)
		{
			Service.InsertProduct(entity);
		}
		protected override void Update(Product entity)
		{
			Service.UpdateProduct(entity);
		}
		protected override void Delete(Product entity)
		{
			Service.DeleteProduct(entity);
		}

		[WebApiQueryable]
		public SingleResult<Product> GetProduct(int key)
		{
			return GetSingleResult(key);
		}		

		// navigation properties

		public DeliveryTime GetDeliveryTime(int key)
		{
			return GetExpandedProperty<DeliveryTime>(key, x => x.DeliveryTime);
		}

		[WebApiQueryable]
		public IQueryable<ProductCategory> GetProductCategories(int key)
		{
			var entity = GetExpandedEntity<ICollection<ProductCategory>>(key, x => x.ProductCategories);

			return entity.ProductCategories.AsQueryable();
		}

		[WebApiQueryable]
		public IQueryable<ProductManufacturer> GetProductManufacturers(int key)
		{
			var entity = GetExpandedEntity<ICollection<ProductManufacturer>>(key, x => x.ProductManufacturers);

			return entity.ProductManufacturers.AsQueryable();
		}

		[WebApiQueryable]
		public IQueryable<ProductPicture> GetProductPictures(int key)
		{
			var entity = GetExpandedEntity<ICollection<ProductPicture>>(key, x => x.ProductPictures);

			return entity.ProductPictures.AsQueryable();
		}

		[WebApiQueryable]
		public IQueryable<ProductSpecificationAttribute> GetProductSpecificationAttributes(int key)
		{
			var entity = GetExpandedEntity<ICollection<ProductSpecificationAttribute>>(key, x => x.ProductSpecificationAttributes);

			return entity.ProductSpecificationAttributes.AsQueryable();
		}

		[WebApiQueryable]
		public IQueryable<ProductTag> GetProductTags(int key)
		{
			var entity = GetExpandedEntity<ICollection<ProductTag>>(key, x => x.ProductTags);

			return entity.ProductTags.AsQueryable();
		}

		[WebApiQueryable]
		public IQueryable<TierPrice> GetTierPrices(int key)
		{
			var entity = GetExpandedEntity<ICollection<TierPrice>>(key, x => x.TierPrices);

			return entity.TierPrices.AsQueryable();
		}

		[WebApiQueryable]
		public IQueryable<Discount> GetAppliedDiscounts(int key)
		{
			var entity = GetExpandedEntity<ICollection<Discount>>(key, x => x.AppliedDiscounts);

			return entity.AppliedDiscounts.AsQueryable();
		}

		[WebApiQueryable]
		public IQueryable<ProductVariantAttribute> GetProductVariantAttributes(int key)
		{
			var entity = GetExpandedEntity<ICollection<ProductVariantAttribute>>(key, x => x.ProductVariantAttributes);

			return entity.ProductVariantAttributes.AsQueryable();
		}

		[WebApiQueryable]
		public IQueryable<ProductVariantAttributeCombination> GetProductVariantAttributeCombinations(int key)
		{
			var entity = GetExpandedEntity<ICollection<ProductVariantAttributeCombination>>(key, x => x.ProductVariantAttributeCombinations);

			return entity.ProductVariantAttributeCombinations.AsQueryable();
		}

		// actions

		//[HttpGet, WebApiQueryable]
		//public IQueryable<RelatedProduct> GetRelatedProducts(int key)
		//{
		//	if (!ModelState.IsValid)
		//		throw this.ExceptionInvalidModelState();

		//	var repository = EngineContext.Current.Resolve<IRepository<RelatedProduct>>();

		//	var query =
		//		from x in repository.Table
		//		where x.ProductId1 == key
		//		select x;

		//	return query;
		//}
	}
}
