﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using SmartStore.Core.Caching;
using SmartStore.Core.Data;
using SmartStore.Core.Domain.Stores;
using SmartStore.Services.Events;
using SmartStore.Services.Localization;

namespace SmartStore.Services.Stores
{
	/// <summary>
	/// Store service
	/// </summary>
	public partial class StoreService : IStoreService
	{
		#region Constants
		private const string STORES_ALL_KEY = "SmartStore.stores.all";
		private const string STORES_PATTERN_KEY = "SmartStore.stores.";
		#endregion

		#region Fields

		private readonly IRepository<Store> _storeRepository;
		private readonly IEventPublisher _eventPublisher;
		private readonly ICacheManager _cacheManager;

		#endregion

		#region Ctor

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="cacheManager">Cache manager</param>
		/// <param name="storeRepository">Store repository</param>
		/// <param name="eventPublisher">Event published</param>
		public StoreService(ICacheManager cacheManager,
			IRepository<Store> storeRepository,
			IEventPublisher eventPublisher)
		{
			this._cacheManager = cacheManager;
			this._storeRepository = storeRepository;
			this._eventPublisher = eventPublisher;
		}

		#endregion

		#region Methods

		/// <summary>
		/// Deletes a store
		/// </summary>
		/// <param name="store">Store</param>
		public virtual void DeleteStore(Store store)
		{
			if (store == null)
				throw new ArgumentNullException("store");

			var allStores = GetAllStores();
			if (allStores.Count == 1)
				throw new Exception("You cannot delete the only configured store.");

			_storeRepository.Delete(store);

			_cacheManager.RemoveByPattern(STORES_PATTERN_KEY);

			//event notification
			_eventPublisher.EntityDeleted(store);
		}

		/// <summary>
		/// Gets all stores
		/// </summary>
		/// <returns>Store collection</returns>
		public virtual IList<Store> GetAllStores()
		{
			string key = STORES_ALL_KEY;
			return _cacheManager.Get(key, () =>
			{
				var query = from s in _storeRepository.Table
							orderby s.DisplayOrder, s.Name
							select s;
				var stores = query.ToList();
				return stores;
			});
		}

		/// <summary>
		/// Gets a store 
		/// </summary>
		/// <param name="storeId">Store identifier</param>
		/// <returns>Store</returns>
		public virtual Store GetStoreById(int storeId)
		{
			if (storeId == 0)
				return null;

            return _storeRepository.GetById(storeId);
		}

		/// <summary>
		/// Inserts a store
		/// </summary>
		/// <param name="store">Store</param>
		public virtual void InsertStore(Store store)
		{
			if (store == null)
				throw new ArgumentNullException("store");

			_storeRepository.Insert(store);

			_cacheManager.RemoveByPattern(STORES_PATTERN_KEY);

			//event notification
			_eventPublisher.EntityInserted(store);
		}

		/// <summary>
		/// Updates the store
		/// </summary>
		/// <param name="store">Store</param>
		public virtual void UpdateStore(Store store)
		{
			if (store == null)
				throw new ArgumentNullException("store");

			_storeRepository.Update(store);

			_cacheManager.RemoveByPattern(STORES_PATTERN_KEY);

			//event notification
			_eventPublisher.EntityUpdated(store);
		}

		/// <summary>
		/// True if there's only one store. Otherwise False.
		/// </summary>
		/// <remarks>codehint: sm-add</remarks>
		public virtual bool IsSingleStoreMode()
		{
			return GetAllStores().Count <= 1;
		}

		#endregion
	}
}