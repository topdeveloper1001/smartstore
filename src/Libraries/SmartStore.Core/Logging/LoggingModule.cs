﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autofac;
using Autofac.Core;
using Autofac.Core.Activators.Reflection;
using SmartStore.ComponentModel;
using SmartStore.Core.Data;

namespace SmartStore.Core.Logging
{
	public class LoggingModule : Autofac.Module
	{
		private readonly ConcurrentDictionary<string, ILogger> _loggerCache;

		public LoggingModule()
		{
			_loggerCache = new ConcurrentDictionary<string, ILogger>(StringComparer.OrdinalIgnoreCase);
		}

		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<Log4netLoggerFactory>().As<ILoggerFactory>().SingleInstance();

			// call CreateLogger in response to the request for an ILogger implementation
			if (DataSettings.DatabaseIsInstalled())
			{
				builder.Register(CreateLogger).As<ILogger>().ExternallyOwned();
			}
			else
			{
				// the install logger should append to a rolling text file only.
				builder.Register(CreateInstallLogger).As<ILogger>().ExternallyOwned();
			}
		}

		protected override void AttachToComponentRegistration(IComponentRegistry componentRegistry, IComponentRegistration registration)
		{
			bool hasCtorLogger = false;
			bool hasPropertyLogger = false;

			FastProperty[] loggerProperties = null;

			var ra = registration.Activator as ReflectionActivator;
			if (ra != null)
			{
				// // Look for ctor parameters of type "ILogger" 
				var ctors = ra.ConstructorFinder.FindConstructors(ra.LimitType);
				var loggerParameters = ctors.SelectMany(ctor => ctor.GetParameters()).Where(pi => pi.ParameterType == typeof(ILogger));
				hasCtorLogger = loggerParameters.Any();

				// Autowire properties
				// Look for settable properties of type "ILogger" 
				loggerProperties = ra.LimitType
					.GetProperties(BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance)
					.Select(p => new
					{
						PropertyInfo = p,
						p.PropertyType,
						IndexParameters = p.GetIndexParameters(),
						Accessors = p.GetAccessors(false)
					})
					.Where(x => x.PropertyType == typeof(ILogger)) // must be a logger
					.Where(x => x.IndexParameters.Count() == 0) // must not be an indexer
					.Where(x => x.Accessors.Length != 1 || x.Accessors[0].ReturnType == typeof(void)) //must have get/set, or only set
					.Select(x => new FastProperty(x.PropertyInfo))
					.ToArray();

				hasPropertyLogger = loggerProperties.Length > 0;

				// Ignore components known to be without logger dependencies
				if (!hasCtorLogger && !hasPropertyLogger)
					return;
			}

			var componentType = registration.Activator.LimitType;

			if (hasCtorLogger)
			{
				registration.Preparing += (sender, args) =>
				{
					var logger = GetCachedLogger(componentType, args.Context);
					args.Parameters = new[] { TypedParameter.From(logger) }.Concat(args.Parameters);
				};
			}

			if (hasPropertyLogger)
			{
				registration.Activating += (sender, args) =>
				{
					var logger = GetCachedLogger(componentType, args.Context);
					foreach (var prop in loggerProperties)
					{
						prop.SetValue(args.Instance, logger);
					}
				};
			}
		}

		private ILogger GetCachedLogger(Type componentType, IComponentContext ctx)
		{
			var logger = _loggerCache.GetOrAdd(componentType.FullName, key => ctx.Resolve<ILogger>(new TypedParameter(typeof(Type), componentType)));
			return logger;
		}

		private static ILogger CreateLogger(IComponentContext context, IEnumerable<Parameter> parameters)
		{
			// return an ILogger in response to Resolve<ILogger>(componentTypeParameter)
			var loggerFactory = context.Resolve<ILoggerFactory>();

			if (parameters != null && parameters.Any())
			{
				var containingType = parameters.TypedAs<Type>();
				return loggerFactory.GetLogger(containingType);
			}
			else
			{
				return loggerFactory.GetLogger("SmartStore");
			}
		}

		private static ILogger CreateInstallLogger(IComponentContext context, IEnumerable<Parameter> parameters)
		{
			return context.Resolve<ILoggerFactory>().GetLogger("Install");
		}
	}
}
