﻿using System;
using SmartStore.Core;
using SmartStore.Core.Domain.DataExchange;
using SmartStore.Core.Plugins;

namespace SmartStore.Services.DataExchange.Export.Providers
{
	/// <summary>
	/// Exports XML formatted product data to a file
	/// </summary>
	[SystemName("Exports.SmartStoreProductXml")]
	[FriendlyName("Product XML Export")]
	public class ProductXmlExportProvider : ExportProviderBase
	{
		public static string SystemName
		{
			get { return "Exports.SmartStoreProductXml"; }
		}

		public override ExportEntityType EntityType
		{
			get { return ExportEntityType.Product; }
		}

		public override string FileExtension
		{
			get { return "XML"; }
		}

		protected override void Export(IExportExecuteContext context)
		{
			using (var helper = new ExportXmlHelper(context.DataStream))
			{
				helper.Writer.WriteStartDocument();
				helper.Writer.WriteStartElement("Products");
				helper.Writer.WriteAttributeString("Version", SmartStoreVersion.CurrentVersion);

				while (context.Abort == ExportAbortion.None && context.Segmenter.ReadNextSegment())
				{
					var segment = context.Segmenter.CurrentSegment;

					foreach (dynamic product in segment)
					{
						if (context.Abort != ExportAbortion.None)
							break;

						try
						{
							helper.WriteProduct(product, "Product");

							++context.RecordsSucceeded;
						}
						catch (Exception exc)
						{
							context.RecordException(exc, (int)product.Id);
						}
					}
				}

				helper.Writer.WriteEndElement();	// Products
				helper.Writer.WriteEndDocument();
			}
		}
	}
}
