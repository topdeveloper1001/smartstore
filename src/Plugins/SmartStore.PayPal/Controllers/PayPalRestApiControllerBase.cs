﻿using System.IO;
using System.Web.Mvc;
using SmartStore.Core.Configuration;
using SmartStore.PayPal.Services;
using SmartStore.PayPal.Settings;
using SmartStore.Web.Framework.Controllers;
using SmartStore.Web.Framework.Security;

namespace SmartStore.PayPal.Controllers
{
	public abstract class PayPalRestApiControllerBase<TSetting> : PaymentControllerBase where TSetting : PayPalApiSettingsBase, ISettings, new()
	{
		public PayPalRestApiControllerBase(
			string systemName,
			IPayPalService payPalService)
		{
			SystemName = systemName;
			PayPalService = payPalService;
		}

		private string GetControllerName()
		{
			return GetType().Name.Replace("Controller", "");
		}

		protected string SystemName { get; private set; }
		protected IPayPalService PayPalService { get; private set; }

		[AdminAuthorize]
		public ActionResult UpsertExperienceProfile()
		{
			var storeScope = this.GetActiveStoreScopeConfiguration(Services.StoreService, Services.WorkContext);
			var settings = Services.Settings.LoadSetting<TSetting>(storeScope);

			var store = Services.StoreService.GetStoreById(storeScope == 0 ? Services.StoreContext.CurrentStore.Id : storeScope);
			var session = new PayPalSessionData();

			var result = PayPalService.EnsureAccessToken(session, settings);
			if (result.Success)
			{
				result = PayPalService.UpsertCheckoutExperience(settings, session, store);
				if (result.Success && result.Id.HasValue())
				{
					settings.ExperienceProfileId = result.Id;
					Services.Settings.SaveSetting(settings, x => x.ExperienceProfileId, storeScope, false);
					Services.Settings.ClearCache();
				}
			}

			if (result.Success)
				NotifySuccess(T("Admin.Common.TaskSuccessfullyProcessed"));
			else
				NotifyError(result.ErrorMessage);

			return RedirectToAction("ConfigureProvider", "Plugin", new { area = "admin", systemName = SystemName });
		}

		[AdminAuthorize]
		public ActionResult DeleteExperienceProfile()
		{
			var storeScope = this.GetActiveStoreScopeConfiguration(Services.StoreService, Services.WorkContext);
			var settings = Services.Settings.LoadSetting<TSetting>(storeScope);
			var session = new PayPalSessionData();

			var result = PayPalService.EnsureAccessToken(session, settings);
			if (result.Success)
			{
				result = PayPalService.DeleteCheckoutExperience(settings, session);
				if (result.Success)
				{
					settings.ExperienceProfileId = null;
					Services.Settings.SaveSetting(settings, x => x.ExperienceProfileId, storeScope, false);
					Services.Settings.ClearCache();					
				}
			}

			if (result.Success)
				NotifySuccess(T("Admin.Common.TaskSuccessfullyProcessed"));
			else
				NotifyError(result.ErrorMessage);

			return RedirectToAction("ConfigureProvider", "Plugin", new { area = "admin", systemName = SystemName });
		}

		[AdminAuthorize]
		public ActionResult CreateWebhook()
		{
			var settings = Services.Settings.LoadSetting<TSetting>();
			var session = new PayPalSessionData();

			if (settings.WebhookId.HasValue())
			{
				var unused = PayPalService.DeleteWebhook(settings, session);

				Services.Settings.SaveSetting(settings, x => x.WebhookId, 0, false);
			}

			var url = Url.Action("Webhook", GetControllerName(), new { area = Plugin.SystemName }, "https");

			var result = PayPalService.EnsureAccessToken(session, settings);
			if (result.Success)
			{
				result = PayPalService.CreateWebhook(settings, session, url);
				if (result.Success)
				{
					settings.WebhookId = result.Id;
					Services.Settings.SaveSetting(settings, x => x.WebhookId, 0, false);
				}
			}

			Services.Settings.ClearCache();

			if (result.Success)
				NotifySuccess(T("Admin.Common.TaskSuccessfullyProcessed"));
			else
				NotifyError(result.ErrorMessage);

			return RedirectToAction("ConfigureProvider", "Plugin", new { area = "admin", systemName = SystemName });
		}

		[AdminAuthorize]
		public ActionResult DeleteWebhook()
		{
			var settings = Services.Settings.LoadSetting<TSetting>();
			var session = new PayPalSessionData();

			if (settings.WebhookId.HasValue())
			{
				var result = PayPalService.EnsureAccessToken(session, settings);
				if (result.Success)
				{
					result = PayPalService.DeleteWebhook(settings, session);
					if (result.Success)
					{
						settings.WebhookId = null;
						Services.Settings.SaveSetting(settings, x => x.WebhookId, 0, false);
						Services.Settings.ClearCache();
					}
				}

				if (result.Success)
					NotifySuccess(T("Admin.Common.TaskSuccessfullyProcessed"));
				else
					NotifyError(result.ErrorMessage);
			}

			return RedirectToAction("ConfigureProvider", "Plugin", new { area = "admin", systemName = SystemName });
		}

		[ValidateInput(false)]
		public ActionResult Webhook()
		{
			string json = null;
			using (var reader = new StreamReader(Request.InputStream))
			{
				json = reader.ReadToEnd();
			}

			var settings = Services.Settings.LoadSetting<TSetting>();

			var result = PayPalService.ProcessWebhook(settings, Request.Headers, json);

			return new HttpStatusCodeResult(result);
		}
	}
}