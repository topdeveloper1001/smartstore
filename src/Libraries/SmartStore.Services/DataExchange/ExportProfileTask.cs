﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;
using Autofac;
using SmartStore.Core;
using SmartStore.Core.Data;
using SmartStore.Core.Domain;
using SmartStore.Core.Domain.Catalog;
using SmartStore.Core.Domain.Customers;
using SmartStore.Core.Domain.DataExchange;
using SmartStore.Core.Domain.Directory;
using SmartStore.Core.Domain.Media;
using SmartStore.Core.Domain.Stores;
using SmartStore.Core.Html;
using SmartStore.Core.Logging;
using SmartStore.Core.Plugins;
using SmartStore.Services.Catalog;
using SmartStore.Services.Customers;
using SmartStore.Services.Directory;
using SmartStore.Services.Localization;
using SmartStore.Services.Media;
using SmartStore.Services.Seo;
using SmartStore.Services.Tasks;
using SmartStore.Services.Tax;
using SmartStore.Utilities;

namespace SmartStore.Services.DataExchange
{
	public class ExportProfileTask : ITask
	{
		private const string _logName = "log.txt";
		private const int _maxErrors = 20;
		private const int _pageSize = 100;

		private ICommonServices _services;
		private IExportService _exportService;
		private IProductService _productService;
		private IPictureService _pictureService;
		private MediaSettings _mediaSettings;
		private IPriceCalculationService _priceCalculationService;
		private ITaxService _taxService;
		private ICurrencyService _currencyService;
		private ICustomerService _customerService;

		#region Utilities

		private void PrepareProductDescription(ExportProfileTaskContext ctx, dynamic expando)
		{
			try
			{
				string description = "";

				// description merging
				if (ctx.Projection.DescriptionMerging.HasValue)
				{
					var type = ctx.Projection.DescriptionMerging ?? ExportDescriptionMergingType.ShortDescriptionOrNameIfEmpty;

					if (type == ExportDescriptionMergingType.ShortDescriptionOrNameIfEmpty)
					{
						description = expando.FullDescription;

						if (description.IsEmpty())
							description = expando.ShortDescription;
						if (description.IsEmpty())
							description = expando.Name;
					}
					else if (type == ExportDescriptionMergingType.ShortDescription)
					{
						description = expando.ShortDescription;
					}
					else if (type == ExportDescriptionMergingType.Description)
					{
						description = expando.FullDescription;
					}
					else if (type == ExportDescriptionMergingType.NameAndShortDescription)
					{
						description = ((string)expando.Name).Grow((string)expando.ShortDescription, " ");
					}
					else if (type == ExportDescriptionMergingType.NameAndDescription)
					{
						description = ((string)expando.Name).Grow((string)expando.FullDescription, " ");
					}
					else if (type == ExportDescriptionMergingType.ManufacturerAndNameAndShortDescription || type == ExportDescriptionMergingType.ManufacturerAndNameAndDescription)
					{
						string name = (string)expando.Name;
						dynamic productManu = ((List<ExpandoObject>)expando.ProductManufacturers).FirstOrDefault();

						if (productManu != null)
						{
							dynamic manu = productManu.Manufacturer;
							description = ((string)manu.Name).Grow(name, " ");

							if (type == ExportDescriptionMergingType.ManufacturerAndNameAndShortDescription)
								description = description.Grow((string)expando.ShortDescription, " ");
							else
								description = description.Grow((string)expando.FullDescription, " ");
						}
					}
				}
				else
				{
					description = expando.FullDescription;
				}

				// append text
				if (ctx.Projection.AppendDescriptionText.HasValue() && ((string)expando.ShortDescription).IsEmpty() && ((string)expando.FullDescription).IsEmpty())
				{
					string[] appendText = ctx.Projection.AppendDescriptionText.SplitSafe(";");
					if (appendText.Length > 0)
					{
						var rnd = (new Random()).Next(0, appendText.Length - 1);

						description = description.Grow(appendText.SafeGet(rnd), " ");
					}
				}

				// remove critical characters
				if (description.HasValue() && ctx.Projection.RemoveCriticalCharacters)
				{
					foreach (var str in ctx.Projection.CriticalCharacters.SplitSafe(";"))
						description = description.Replace(str, "");
				}

				// convert to plain text
				if (ctx.Projection.DescriptionToPlainText)
				{
					//Regex reg = new Regex("<[^>]+>", RegexOptions.IgnoreCase);
					//description = HttpUtility.HtmlDecode(reg.Replace(description, ""));

					description = HtmlUtils.ConvertHtmlToPlainText(description);
					description = HtmlUtils.StripTags(HttpUtility.HtmlDecode(description));
				}

				expando.FullDescription = description;
			}
			catch { }
		}

		private void PrepareProductPrice(ExportProfileTaskContext ctx, dynamic expando, Product product)
		{
			decimal price = decimal.Zero;

			// price type
			if (ctx.Projection.PriceType.HasValue)
			{
				var type = ctx.Projection.PriceType ?? PriceDisplayType.PreSelectedPrice;

				if (type == PriceDisplayType.LowestPrice)
				{
					bool displayFromMessage;
					price = _priceCalculationService.GetLowestPrice(product, null, out displayFromMessage);
				}
				else if (type == PriceDisplayType.PreSelectedPrice)
				{
					price = _priceCalculationService.GetPreselectedPrice(product, null);
				}
				else if (type == PriceDisplayType.PriceWithoutDiscountsAndAttributes)
				{
					price = _priceCalculationService.GetFinalPrice(product, null, ctx.ProjectionCustomer, decimal.Zero, false, 1, null, null);
				}
			}
			else
			{
				price = expando.Price;
			}

			// convert net to gross
			if (ctx.Projection.ConvertNetToGrossPrices)
			{
				decimal taxRate;
				price = _taxService.GetProductPrice(product, price, true, ctx.ProjectionCustomer, out taxRate);
			}

			if (price != decimal.Zero)
			{
				price = _currencyService.ConvertFromPrimaryStoreCurrency(price, ctx.ProjectionCurrency, ctx.Store);
			}

			expando.Price = price;
		}

		#endregion

		private void InitDependencies(TaskExecutionContext context)
		{
			_services = context.Resolve<ICommonServices>();
			_exportService = context.Resolve<IExportService>();
			_productService = context.Resolve<IProductService>();
			_pictureService = context.Resolve<IPictureService>();
			_mediaSettings = context.Resolve<MediaSettings>();
			_priceCalculationService = context.Resolve<IPriceCalculationService>();
			_taxService = context.Resolve<ITaxService>();
			_currencyService = context.Resolve<ICurrencyService>();
			_customerService = context.Resolve<ICustomerService>();
		}

		private IEnumerable<Product> GetProducts(ExportProfileTaskContext ctx, int pageIndex)
		{
			_services.DbContext.DetachAll();

			if (!ctx.Cancellation.IsCancellationRequested)
			{
				var searchContext = new ProductSearchContext
				{
					OrderBy = ProductSortingEnum.CreatedOn,
					PageIndex = pageIndex,
					PageSize = _pageSize,
					StoreId = (ctx.Profile.PerStore ? ctx.Store.Id : 0),
					VisibleIndividuallyOnly = true
				};

				var products = _productService.SearchProducts(searchContext);

				foreach (var product in products)
				{
					if (product.ProductType == ProductType.SimpleProduct || product.ProductType == ProductType.BundledProduct)
					{
						yield return product;
					}
					else if (product.ProductType == ProductType.GroupedProduct)
					{
						var associatedSearchContext = new ProductSearchContext
						{
							OrderBy = ProductSortingEnum.CreatedOn,
							PageSize = int.MaxValue,
							StoreId = (ctx.Profile.PerStore ? ctx.Store.Id : 0),
							VisibleIndividuallyOnly = false,
							ParentGroupedProductId = product.Id
						};

						foreach (var associatedProduct in _productService.SearchProducts(associatedSearchContext))
						{
							yield return associatedProduct;
						}
					}
				}
			}
		}

		private ExpandoObject ToExpando(ExportProfileTaskContext ctx, Product product)
		{
			dynamic expando = product.ToExpando(ctx.Projection.LanguageId ?? 0, _pictureService, _mediaSettings, ctx.Store);

			PrepareProductDescription(ctx, expando);
			PrepareProductPrice(ctx, expando, product);

			string brand = null;

			var manu = (product.ProductManufacturers as List<dynamic>).FirstOrDefault();
			if (manu != null)
				brand = manu.Manufacturer.Name;
			if (brand.IsEmpty())
				brand = ctx.Projection.Brand;

			expando._Brand = brand;

			return expando as ExpandoObject;
		}

		private void Cleanup(ExportProfileTaskContext ctx)
		{
			if (!ctx.Profile.Cleanup)
				return;

			FileSystemHelper.ClearDirectory(ctx.Folder, false, new List<string> { _logName });
			
			// TODO: more deployment specific here
		}

		private void ExportCoreInner(ExportProfileTaskContext ctx, Store store)
		{
			ctx.Store = store;
			ctx.Export.StoreId = store.Id;
			ctx.Export.StoreUrl = store.Url;

			// be careful with too long file system paths
			ctx.Export.FileNamePattern = string.Concat(
				"{0}-",
				ctx.Profile.PerStore ? SeoHelper.GetSeName(store.Name, true, false).ToValidFileName("").Truncate(20) : "all-stores",
				"{1}",
				ctx.Provider.Value.FileExtension.ToLower().EnsureStartsWith(".")
			);

			{
				var logHead = new StringBuilder();
				logHead.AppendLine();
				logHead.AppendLine(new string('-', 40));
				logHead.AppendLine("SmartStore.NET:\t\tv." + SmartStoreVersion.CurrentFullVersion);
				logHead.AppendLine("Export profile:\t\t{0} (Id {1})".FormatInvariant(ctx.Profile.Name, ctx.Profile.Id));

				var plugin = ctx.Provider.Metadata.PluginDescriptor;
				logHead.Append("Plugin:\t\t\t\t");
				logHead.AppendLine(plugin == null ? "".NaIfEmpty() : "{0} ({1}) v.{2}".FormatInvariant(plugin.FriendlyName, plugin.SystemName, plugin.Version.ToString()));

				logHead.AppendLine("Export provider:\t{0} ({1})".FormatInvariant(ctx.Provider == null ? "".NaIfEmpty() : ctx.Provider.Metadata.FriendlyName, ctx.Profile.ProviderSystemName));

				var storeInfo = (ctx.Profile.PerStore ? "{0} (Id {1})".FormatInvariant(store.Name, store.Id) : "all stores");
				logHead.Append("Store:\t\t\t\t" + storeInfo);

				ctx.Log.Information(logHead.ToString());
			}

			if (ctx.Provider == null)
			{
				throw new SmartException("Export aborted because the export provider cannot be loaded");
			}

			if (!ctx.Provider.IsValid())
			{
				throw new SmartException("Export aborted because the export provider is not valid");
			}

			if (ctx.Provider.Value.EntityType == ExportEntityType.Product)
			{
				var anySingleProduct = _productService.SearchProducts(new ProductSearchContext
				{
					OrderBy = ProductSortingEnum.CreatedOn,
					PageIndex = ctx.Profile.Offset,
					PageSize = 1,
					StoreId = (ctx.Profile.PerStore ? ctx.Store.Id : 0),
					VisibleIndividuallyOnly = true
				});

				ctx.Segmenter = new ExportSegmenter(
					pageIndex => GetProducts(ctx, pageIndex),
					entity => ToExpando(ctx, (Product)entity),
					new PagedList(ctx.Profile.Offset, ctx.Profile.Limit, _pageSize, anySingleProduct.TotalCount),
					ctx.Profile.BatchSize
				);
			}


			if (ctx.Segmenter == null)
			{
				throw new SmartException("Unsupported entity type '{0}'".FormatInvariant(ctx.Provider.Value.EntityType.ToString()));
			}
			else
			{
				ctx.Export.Data = ctx.Segmenter;

				ctx.Segmenter.Start(() =>
				{
					if (!ctx.Provider.Value.Execute(ctx.Export))
						return false;

					return !ctx.Cancellation.IsCancellationRequested;
				});
			}
		}

		private void ExportCoreOuter(ExportProfileTaskContext ctx)
		{
			if (ctx.Profile == null || !ctx.Profile.Enabled)
				return;

			try
			{
				FileSystemHelper.ClearDirectory(ctx.Folder, false);

				using (var scope = new DbContextScope(autoDetectChanges: false, validateOnSave: false, forceNoTracking: true))
				using (var logger = new TraceLogger(Path.Combine(ctx.Folder, _logName)))
				{
					ctx.Log = logger;
					ctx.Export.Log = logger;

					if (ctx.Projection.CurrencyId.HasValue)
						ctx.ProjectionCurrency = _currencyService.GetCurrencyById(ctx.Projection.CurrencyId.Value);
					else
						ctx.ProjectionCurrency = _services.WorkContext.WorkingCurrency;

					if (ctx.Projection.CustomerId.HasValue)
						ctx.ProjectionCustomer = _customerService.GetCustomerById(ctx.Projection.CustomerId.Value);
					else
						ctx.ProjectionCustomer = _services.WorkContext.CurrentCustomer;

					// TODO: log number of flown out records

					if (ctx.Profile.PerStore)
					{
						var allStores = _services.StoreService.GetAllStores();

						foreach (var store in allStores.Where(x => ctx.Filter.StoreId == 0 || ctx.Filter.StoreId == x.Id))
						{
							ExportCoreInner(ctx, store);
						}
					}
					else
					{
						ExportCoreInner(ctx, _services.StoreContext.CurrentStore);
					}
				}
			}
			catch (Exception exc)
			{
				ctx.Log.Error(exc);
			}
			finally
			{
				try
				{
					if (ctx.Cancellation.IsCancellationRequested)
						ctx.Log.Warning("Export aborted. A cancellation has been requested");

					if (ctx.Segmenter != null)
						ctx.Segmenter.Dispose();

					Cleanup(ctx);
				}
				catch { }
			}
		}


		public void Execute(TaskExecutionContext context)
		{
			InitDependencies(context);

			var profileId = context.ScheduleTask.Alias.ToInt();
			var profile = _exportService.GetExportProfileById(profileId);

			var ctx = new ExportProfileTaskContext(profile, _exportService.LoadProvider(profile.ProviderSystemName), context.CancellationToken);

			ExportCoreOuter(ctx);
		}

		// TODO: is method required?
		public void Execute(ExportProfile profile, IComponentContext context)
		{
			if (profile == null)
				throw new ArgumentNullException("profile");

			if (context == null)
				throw new ArgumentNullException("context");

			InitDependencies(new TaskExecutionContext(context, null));

			var cancellation = new CancellationTokenSource(TimeSpan.FromHours(4.0));

			var ctx = new ExportProfileTaskContext(profile, _exportService.LoadProvider(profile.ProviderSystemName), cancellation.Token);

			ExportCoreOuter(ctx);
		}
	}


	internal class ExportProfileTaskContext
	{
		public ExportProfileTaskContext(ExportProfile profile, Provider<IExportProvider> provider, CancellationToken cancellation)
		{
			Debug.Assert(profile.FolderName.HasValue(), "Folder name must not be empty.");

			Profile = profile;
			Provider = provider;
			Filter = XmlHelper.Deserialize<ExportFilter>(profile.Filtering);
			Projection = XmlHelper.Deserialize<ExportProjection>(profile.Projection);
			Cancellation = cancellation;
			Folder = FileSystemHelper.TempDir(@"Profile\Export\{0}".FormatInvariant(profile.FolderName));

			Export = new ExportExecuteContext(Cancellation, Folder);
		}

		public ExportProfile Profile { get; private set; }
		public Provider<IExportProvider> Provider { get; private set; }
		public ExportFilter Filter { get; private set; }

		public ExportProjection Projection { get; private set; }
		public Currency ProjectionCurrency { get; set; }
		public Customer ProjectionCustomer { get; set; }

		public CancellationToken Cancellation { get; private set; }
		public TraceLogger Log { get; set; }
		public Store Store { get; set; }

		public string Folder { get; private set; }

		public ExportSegmenter Segmenter { get; set; }

		public ExportExecuteContext Export { get; set; }
	}
}
