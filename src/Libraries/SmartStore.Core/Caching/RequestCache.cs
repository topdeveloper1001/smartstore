﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace SmartStore.Core.Caching
{
	public class RequestCache : IRequestCache
	{
		const string RegionName = "SmartStoreNET:";

		private IDictionary _emptyDictionary = new Dictionary<string, object>();

		private readonly HttpContextBase _context;

		public RequestCache(HttpContextBase context)
		{
			_context = context;
		}

		public T Get<T>(string key)
		{
			return Get<T>(key, null);
		}

		public T Get<T>(string key, Func<T> acquirer)
		{
			var items = GetItems();

			key = BuildKey(key);

			if (items.Contains(key))
			{
				return (T)items[key];
			}

			if (acquirer != null)
			{
				var value = acquirer();
				items.Add(key, value);
				return value;
			}

			return default(T);
		}

		public void Set(string key, object value)
		{
			var items = GetItems();

			key = BuildKey(key);
			
			if (items.Contains(key))
				items[key] = value;
			else
				items.Add(key, value);
		}

		public void Clear()
		{
			RemoveByPattern("*");
		}

		public bool Contains(string key)
		{
			return GetItems().Contains(BuildKey(key));
		}

		public void Remove(string key)
		{
			GetItems().Remove(BuildKey(key));
		}

		public void RemoveByPattern(string pattern)
		{
			var items = GetItems();

			var keysToRemove = Keys(pattern).ToArray();

			foreach (string key in keysToRemove)
			{
				items.Remove(key);
			}
		}

		protected IDictionary GetItems()
		{
			return _context.Items ?? _emptyDictionary;
		}

		public IEnumerable<string> Keys(string pattern)
		{
			var items = GetItems();

			if (items.Count == 0)
				yield break;

			var matcher = pattern == "*" ? null : CreateMatcher(pattern);

			var enumerator = items.GetEnumerator();
			while (enumerator.MoveNext())
			{
				string key = enumerator.Key as string;
				if (key == null)
					continue;
				if (key.StartsWith(RegionName))
				{
					key = key.Substring(RegionName.Length);
					if (matcher == null || matcher.IsMatch(key))
					{
						yield return key;
					}
				}
			}
		}

		private string BuildKey(string key)
		{
			return RegionName + key.EmptyNull();
		}

		private static Regex CreateMatcher(string pattern)
		{
			return new Regex(pattern, RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);
		}
	}
}
