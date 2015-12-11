﻿using SmartStore.Core.Domain.DataExchange;
using SmartStore.Core.Domain.Tasks;

namespace SmartStore.Core.Domain
{
	public class ImportProfile : BaseEntity
	{
		/// <summary>
		/// The name of the profile
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The name of the folder (file system)
		/// </summary>
		public string FolderName { get; set; }

		/// <summary>
		/// The name of the initially uploaded import file
		/// </summary>
		public string FileName { get; set; }

		/// <summary>
		/// The identifier of the entity type
		/// </summary>
		public int EntityTypeId { get; set; }

		/// <summary>
		/// The entity type
		/// </summary>
		public ImportEntityType EntityType
		{
			get
			{
				return (ImportEntityType)EntityTypeId;
			}
			set
			{
				EntityTypeId = (int)value;
			}
		}

		/// <summary>
		/// Whether the profile is enabled
		/// </summary>
		public bool Enabled { get; set; }

		/// <summary>
		/// Number of records to bypass
		/// </summary>
		public int Skip { get; set; }

		/// <summary>
		/// Maximum number of records to return
		/// </summary>
		public int Take { get; set; }

		/// <summary>
		/// File type specific configuration
		/// </summary>
		public string FileTypeConfiguration { get; set; }

		/// <summary>
		/// Mapping of import columns
		/// </summary>
		public string ColumnMapping { get; set; }

		/// <summary>
		/// The scheduling task identifier
		/// </summary>
		public int SchedulingTaskId { get; set; }

		/// <summary>
		/// The scheduling task
		/// </summary>
		public virtual ScheduleTask ScheduleTask { get; set; }
	}
}
