﻿﻿using SmartStore.Core;
using SmartStore.Web.Framework;
using SmartStore.Web.Framework.Mvc;

namespace SmartStore.Admin.Models.Cms
{
	public class WidgetModel : ProviderModel, IActivatable
    {
		[SmartResourceDisplayName("Common.IsActive")]
        public bool IsActive { get; set; }

    }
}