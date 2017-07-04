﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Caching;
using System.Web.Hosting;

namespace SmartStore.Web.Framework.Plugins
{
	public class PluginDebugViewVirtualPathProvider : VirtualPathProvider
	{
		private readonly static ConcurrentDictionary<string, string> _cachedDebugFilePaths = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly DirectoryInfo _pluginsDebugDir;
		private readonly VirtualPathProvider _previous;

		public PluginDebugViewVirtualPathProvider(VirtualPathProvider previous)
		{
			_previous = previous;

			var appRootPath = HostingEnvironment.MapPath("~/").EnsureEndsWith("\\");
			var debugPath = Path.GetFullPath(Path.Combine(appRootPath, @"..\..\Plugins"));
			if (Directory.Exists(debugPath))
			{
				_pluginsDebugDir = new DirectoryInfo(debugPath);
			}
		}

		public override bool FileExists(string virtualPath)
		{
			// Require files in production path to exist, do never fallback to dev path.
			// Doing so could lead to deployment errors (e.g. forgetting to copy a view file to production folder)
			return _previous.FileExists(virtualPath);
		}

		public override VirtualFile GetFile(string virtualPath)
		{
			string appRelativePath;
			if (!IsPluginPath(virtualPath, out appRelativePath))
			{
				return _previous.GetFile(virtualPath);
			}

			string debugPath = ResolveDebugFilePath(appRelativePath);
			return debugPath != null
				? new DebugPluginVirtualFile(virtualPath, debugPath)
				: _previous.GetFile(virtualPath);
		}

		public override CacheDependency GetCacheDependency(string virtualPath, IEnumerable virtualPathDependencies, DateTime utcStart)
		{
			string appRelativePath;
			if (!IsPluginPath(virtualPath, out appRelativePath))
			{
				return _previous.GetCacheDependency(virtualPath, virtualPathDependencies, utcStart);
			}

			string debugPath = ResolveDebugFilePath(appRelativePath);
			return debugPath != null 
				? new CacheDependency(debugPath) 
				: _previous.GetCacheDependency(virtualPath, virtualPathDependencies, utcStart);
		}

		public override string GetFileHash(string virtualPath, IEnumerable virtualPathDependencies)
		{
			string appRelativePath;
			if (!IsPluginPath(virtualPath, out appRelativePath))
			{
				return _previous.GetFileHash(virtualPath, virtualPathDependencies);
			}

			string debugPath = ResolveDebugFilePath(appRelativePath);
			return debugPath != null
				? File.GetLastWriteTime(debugPath).ToString()
				: _previous.GetFileHash(virtualPath, virtualPathDependencies);
		}

		private string ResolveDebugFilePath(string appRelativePath)
		{
			return _cachedDebugFilePaths.GetOrAdd(appRelativePath, FindDebugFile);
		}

		private string FindDebugFile(string appRelativePath)
		{
			if (_pluginsDebugDir == null)
				return null;
			
			var unrooted = appRelativePath.Substring(10); // strip "~/Plugins/"
			string area = unrooted.Substring(0, unrooted.IndexOf('/'));

			// get "Views/Something/View.cshtml"
			var viewPath = unrooted.Substring(area.Length + 1);

			var foldersToCheck = new[] { area, area + "-sym" };

			foreach (var folder in foldersToCheck)
			{
				var pluginDir = new DirectoryInfo(Path.Combine(_pluginsDebugDir.FullName, folder));
				if (pluginDir != null && pluginDir.Exists)
				{
					var result = Path.Combine(pluginDir.FullName, viewPath).Replace("/", "\\");
					return File.Exists(result) ? result : null;
				}
			}

			return null;
		}

		private static bool IsPluginPath(string virtualPath, out string appRelativePath)
		{
			appRelativePath = VirtualPathUtility.ToAppRelative(virtualPath);
			var result = appRelativePath.StartsWith("~/Plugins/", StringComparison.InvariantCultureIgnoreCase);
			return result;
		}
	}

	internal class DebugPluginVirtualFile : VirtualFile
	{
		private readonly string _debugPath;

		public DebugPluginVirtualFile(string virtualPath, string debugPath)
			: base(virtualPath)
		{
			this._debugPath = debugPath;
		}

		public string PhysicalPath
		{
			get { return _debugPath; }
		}

		public override bool IsDirectory
		{
			get { return false; }
		}
		
		public override Stream Open()
		{
			var fileView = new FileStream(_debugPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			return fileView;
		}
	}
}
