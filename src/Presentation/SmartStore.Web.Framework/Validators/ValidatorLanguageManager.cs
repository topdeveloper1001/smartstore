﻿using System;
using System.Globalization;
using System.Web.Hosting;
using FluentValidation;
using FluentValidation.Resources;
using SmartStore.Core.Infrastructure;
using SmartStore.Services;

namespace SmartStore.Web.Framework.Validators
{
	public class ValidatorLanguageManager : LanguageManager
	{
		public override string GetString(string key, CultureInfo culture = null)
		{
			string result = base.GetString(key, culture);

			if (HostingEnvironment.IsHosted)
			{
				// (Perf) although FV expects a culture parameter, we gonna ignore it.
				// It's highly unlikely that it is anything different than our WorkingLanguage.
				var services = EngineContext.Current.Resolve<ICommonServices>();
				result = services.Localization.GetResource("Validation." + key, logIfNotFound: false, defaultValue: result, returnEmptyIfNotFound: true);
			}

			return result;
		}
	}
}
