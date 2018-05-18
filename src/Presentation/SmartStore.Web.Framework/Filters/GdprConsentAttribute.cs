﻿using SmartStore.Core;
using SmartStore.Core.Domain.Customers;
using SmartStore.Core.Logging;
using SmartStore.Services;
using SmartStore.Services.Common;
using SmartStore.Web.Framework.UI;
using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace SmartStore.Web.Framework.Filters
{
	public class GdprConsentAttribute : FilterAttribute, IActionFilter, IResultFilter
	{
		public Lazy<ICommonServices> Services { get; set; }
		public Lazy<PrivacySettings> PrivacySettings { get; set; }
		public Lazy<IGenericAttributeService> GenericAttributeService { get; set; }
		public Lazy<IWidgetProvider> WidgetProvider { get; set; }
		public Lazy<INotifier> Notifier { get; set; }

		public void OnActionExecuting(ActionExecutingContext filterContext)
		{
			if (!PrivacySettings.Value.DisplayGdprConsentOnForms)
				return;

			if (filterContext?.ActionDescriptor == null || filterContext?.HttpContext?.Request == null)
				return;

			if (filterContext.HttpContext.Request.HttpMethod.Equals("GET"))
				return;

			var customer = Services.Value.WorkContext.CurrentCustomer;
			var hasConsentedToGdpr = filterContext.HttpContext.Request.Form["GdprConsent"];
			
			if (filterContext.HttpContext.Request.HttpMethod.Equals("POST") && hasConsentedToGdpr != null)
			{
				if (hasConsentedToGdpr.Contains("true"))
				{
					GenericAttributeService.Value.SaveAttribute(customer, SystemCustomerAttributeNames.HasConsentedToGdpr, true);
				}
				else
				{
					if (!filterContext.HttpContext.Request.IsAjaxRequest())
					{
						// add a validation message
						filterContext.Controller.ViewData.ModelState.AddModelError("", Services.Value.Localization.GetResource("GdprConsent.ValidationMessage"));
					}
					else
					{
						// notify
						Notifier.Value.Error(Services.Value.Localization.GetResource("GdprConsent.ValidationMessage"));
					}
					
					return;
				}
			}
		}

		public void OnActionExecuted(ActionExecutedContext filterContext)
		{

		}

		public void OnResultExecuting(ResultExecutingContext filterContext)
		{
			if (!PrivacySettings.Value.DisplayGdprConsentOnForms)
				return;

			if (filterContext.IsChildAction)
				return;

			var result = filterContext.Result;

			// should only run on a full view rendering result or HTML ContentResult
			if (!result.IsHtmlViewResult())
				return;
			
			WidgetProvider.Value.RegisterAction(
				new[] { "gdpr_consent" },
				"GdprConsent",
				"Common",
				new { area = "" });
		}

		public void OnResultExecuted(ResultExecutedContext filterContext)
		{
		}
	}
}