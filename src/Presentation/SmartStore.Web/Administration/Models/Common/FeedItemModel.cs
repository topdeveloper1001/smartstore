﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using SmartStore.Web.Framework.Mvc;

namespace SmartStore.Admin.Models.Common
{
	public class FeedItemModel : ModelBase
	{
		public string Title { get; set; }
		public string Summary { get; set; }
		public string Link { get; set; }
		public string PublishDate { get; set; }
	}
}