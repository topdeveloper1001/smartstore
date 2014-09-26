﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.Web.Routing;
using SmartStore.Core.Domain.Localization;
using SmartStore.Core.Localization;
using SmartStore.Core.Plugins;
using SmartStore.Services;
using SmartStore.Services.Shipping;
using SmartStore.Utilities;
using SmartStore.Web.Framework.Mvc;

namespace SmartStore.Web.Framework.Plugins
{
	public class PluginMediator
	{
		//private static readonly ConcurrentDictionary<Tuple<PluginDescriptor, string>, string> _iconsMap = new ConcurrentDictionary<Tuple<PluginDescriptor, string>, string>();
		private static readonly ConcurrentDictionary<string, RouteInfo> _routesCache = new ConcurrentDictionary<string, RouteInfo>();
		private readonly ICommonServices _services;

		public PluginMediator(ICommonServices services)
		{
			this._services = services;
			T = NullLocalizer.Instance;
		}

		public Localizer T { get; set; }

		public string GetLocalizedFriendlyName(ProviderMetadata metadata, int languageId = 0, bool returnDefaultValue = true)
		{
			return GetLocalizedValue(metadata, "FriendlyName", x => x.FriendlyName, languageId, returnDefaultValue);
		}

		public string GetLocalizedDescription(ProviderMetadata metadata, int languageId = 0, bool returnDefaultValue = true)
		{
			return GetLocalizedValue(metadata, "Description", x => x.Description, languageId, returnDefaultValue);
		}

		public string GetLocalizedValue(ProviderMetadata metadata,
			string propertyName,
			Expression<Func<ProviderMetadata, string>> fallback,
			int languageId = 0,
			bool returnDefaultValue = true)
		{
			Guard.ArgumentNotNull(() => metadata);

			string systemName = metadata.SystemName;
			var resourceName = metadata.ResourceKeyPattern.FormatInvariant(metadata.SystemName, propertyName);
			string result = _services.Localization.GetResource(resourceName, languageId, false, "", true);

			if (result.IsEmpty() && returnDefaultValue)
				result = fallback.Compile()(metadata);

			return result;
		}

		public void SaveLocalizedValue(ProviderMetadata metadata, int languageId, string propertyName, string value)
		{
			Guard.ArgumentNotNull(() => metadata);
			Guard.ArgumentIsPositive(languageId, "languageId");
			Guard.ArgumentNotEmpty(() => propertyName);

			var resourceName = metadata.ResourceKeyPattern.FormatInvariant(metadata.SystemName, propertyName);
			var resource = _services.Localization.GetLocaleStringResourceByName(resourceName, languageId, false);

			if (resource != null)
			{
				if (value.IsEmpty())
				{
					// delete
					_services.Localization.DeleteLocaleStringResource(resource);
				}
				else
				{
					// update
					resource.ResourceValue = value;
					_services.Localization.UpdateLocaleStringResource(resource);
				}
				_services.Localization.ClearCache();
			}
			else
			{
				if (value.HasValue())
				{
					// insert
					resource = new LocaleStringResource()
					{
						LanguageId = languageId,
						ResourceName = resourceName,
						ResourceValue = value,
					};
					_services.Localization.InsertLocaleStringResource(resource);
					_services.Localization.ClearCache();
				}
			}
		}

		public int? GetUserDisplayOrder(ProviderMetadata metadata)
		{
			return GetSetting<int?>(metadata, "DisplayOrder");
		}

		public T GetSetting<T>(ProviderMetadata metadata, string propertyName)
		{
			var settingKey = metadata.SettingKeyPattern.FormatInvariant(metadata.SystemName, propertyName);
			return _services.Settings.GetSettingByKey<T>(settingKey);
		}

		public void SetUserDisplayOrder(ProviderMetadata metadata, int displayOrder)
		{
			Guard.ArgumentNotNull(() => metadata);

			metadata.DisplayOrder = displayOrder;
			SetSetting(metadata, "DisplayOrder", displayOrder);
		}

		public void SetSetting<T>(ProviderMetadata metadata, string propertyName, T value)
		{
			Guard.ArgumentNotNull(() => metadata);
			Guard.ArgumentNotEmpty(() => propertyName);

			var settingKey = metadata.SettingKeyPattern.FormatInvariant(metadata.SystemName, propertyName);

			if (value != null)
			{
				_services.Settings.SetSetting<T>(settingKey, value, 0, false);
			}
			else
			{
				_services.Settings.DeleteSetting(settingKey);
			}
			
			_services.Settings.ClearCache();
		}

		public ProviderModel ToProviderModel(Provider<IProvider> provider, bool forEdit = false, Action<Provider<IProvider>, ProviderModel> enhancer = null)
		{
			return ToProviderModel<IProvider, ProviderModel>(provider, forEdit, enhancer);
		}

		public TModel ToProviderModel<TProvider, TModel>(Provider<TProvider> provider, bool forEdit = false, Action<Provider<TProvider>, TModel> enhancer = null)
			where TModel : ProviderModel, new()
			where TProvider : IProvider
		{
			Guard.ArgumentNotNull(() => provider);

			var metadata = provider.Metadata;
			var model = new TModel();
			model.SystemName = metadata.SystemName;
			model.FriendlyName = forEdit ? metadata.FriendlyName : GetLocalizedFriendlyName(metadata);
			model.Description = forEdit ? metadata.Description : GetLocalizedDescription(metadata);
			model.DisplayOrder = metadata.DisplayOrder;
			model.IsEditable = metadata.IsEditable;
			model.IconUrl = GetIconUrl(metadata);

			if (metadata.IsConfigurable)
			{
				var routeInfo = _routesCache.GetOrAdd(model.SystemName, (key) =>
				{
					string actionName, controllerName;
					RouteValueDictionary routeValues;
					var configurable = (IConfigurable)provider.Value;
					configurable.GetConfigurationRoute(out actionName, out controllerName, out routeValues);

					if (actionName.IsEmpty())
					{
						metadata.IsConfigurable = false;
						return null;
					}
					else
					{
						return new RouteInfo(actionName, controllerName, routeValues);
					}
				});

				if (routeInfo != null)
				{
					model.ConfigurationRoute = new RouteInfo(routeInfo);
				}
			}

			if (enhancer != null)
			{
				enhancer(provider, model);
			}

			model.IsConfigurable = metadata.IsConfigurable;

			return model;
		}

		public string GetIconUrl(ProviderMetadata metadata)
		{
			var plugin = metadata.PluginDescriptor;

			if (plugin == null)
			{
				return GetDefaultIconUrl(metadata.GroupName);
			}

			return GetIconUrl(plugin, metadata.SystemName);
		}

		/// <summary>
		/// Returns the absolute path of a plugin/provider icon
		/// </summary>
		/// <param name="plugin">The plugin descriptor. Used to resolve the physical path</param>
		/// <param name="providerSystemName">Optional system name of provider. If passed, an icon with this name gets being tried to resolve first.</param>
		/// <returns>The icon's absolute path</returns>
		public string GetIconUrl(PluginDescriptor plugin, string providerSystemName = null)
		{
			//var cacheKey = new Tuple<PluginDescriptor, string>(plugin, providerSystemName);
			
			if (providerSystemName.HasValue())
			{
				if (File.Exists(Path.Combine(plugin.PhysicalPath, "Content", "icon-{0}.png".FormatInvariant(providerSystemName))))
				{
					return "~/Plugins/{0}/Content/icon-{1}.png".FormatInvariant(plugin.SystemName, providerSystemName);
				}
			}
			
			if (File.Exists(Path.Combine(plugin.PhysicalPath, "Content", "icon.png")))
			{
				return "~/Plugins/{0}/Content/icon.png".FormatInvariant(plugin.SystemName);
			}
			else
			{
				return GetDefaultIconUrl(plugin.Group);
			}
		}

		public string GetDefaultIconUrl(string groupName)
		{
			if (groupName.HasValue())
			{
				string path = "~/Administration/Content/images/icon-plugin-{0}.png".FormatInvariant(groupName.ToLower());
				if (File.Exists(CommonHelper.MapPath(path, false)))
				{
					return path;
				}
			}

			return "~/Administration/Content/images/icon-plugin-default.png";
		}

	}
}
