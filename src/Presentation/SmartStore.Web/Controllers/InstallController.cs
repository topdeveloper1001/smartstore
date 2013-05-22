﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Caching;
using System.Web.Hosting;
using System.Web.Mvc;
using System.ComponentModel.Composition;
using SmartStore.Core;
using SmartStore.Core.Caching;
using SmartStore.Core.Data;
using SmartStore.Core.Domain.Localization;
using SmartStore.Core.Infrastructure;
using SmartStore.Core.Plugins;
using SmartStore.Services.Installation;
using SmartStore.Services.Security;
using SmartStore.Web.Framework.Security;
using SmartStore.Web.Infrastructure.Installation;
using SmartStore.Web.Models.Install;
using System.Configuration;

namespace SmartStore.Web.Controllers
{

    public partial class InstallController : AsyncController
    {
        #region Fields

        private readonly IInstallationLocalizationService _locService;
        // codehint: sm-add
        private const string InstallLockStorageKey = "__smnet__install__lock__";
        private const string InstallResultStorageKey = "__smnet__install__result__";
        private float _totalSteps;
        private float _currentStep;

        #endregion

        #region Ctor

        public InstallController(
            IInstallationLocalizationService locService)
        {
            this._locService = locService;
        }

        #endregion
        
        #region Utilities

        // codehint: sm-add
        private InstallationResult GetInstallResult()
        {
            InstallationResult result = null;
            if (HttpRuntime.Cache[InstallResultStorageKey] == null)
            {
                result = new InstallationResult();
                HttpRuntime.Cache.Add(InstallResultStorageKey, result, null, DateTime.Now.AddMinutes(30), Cache.NoSlidingExpiration, CacheItemPriority.Normal, null);
                return result;
            }

            return HttpRuntime.Cache[InstallResultStorageKey] as InstallationResult;
        }

        private InstallationResult UpdateResult(Action<InstallationResult> fn)
        {
            var result = GetInstallResult();
            fn(result);
            HttpRuntime.Cache[InstallResultStorageKey] = result;
            return result;
        }

        private void IncreaseProgress()
        {
            _currentStep++;
            int progress = (int)(((_currentStep / _totalSteps) * 100) / 2) + 50;
            UpdateResult(x => x.Progress = progress);
        }

        /// <summary>
        /// Checks if the specified database exists, returns true if database exists
        /// </summary>
        /// <param name="connectionString">Connection string</param>
        /// <returns>Returns true if the database exists.</returns>
        [NonAction]
        protected bool SqlServerDatabaseExists(string connectionString)
        {
            try
            {
                //just try to connect
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                }
                return true;
            }
            catch 
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a database on the server.
        /// </summary>
        /// <param name="connectionString">Connection string</param>
        /// <param name="collation">Server collation; the default one will be used if not specified</param>
        /// <returns>Error</returns>
        [NonAction]
        protected string CreateDatabase(string connectionString, string collation)
        {
            try
            {
                //parse database name
                var builder = new SqlConnectionStringBuilder(connectionString);
                var databaseName = builder.InitialCatalog;
                //now create connection string to 'master' dabatase. It always exists.
                builder.InitialCatalog = "master";
                var masterCatalogConnectionString = builder.ToString();
                string query = string.Format("CREATE DATABASE [{0}]", databaseName);
                if (!String.IsNullOrWhiteSpace(collation))
                    query = string.Format("{0} COLLATE {1}", query, collation);
                using (var conn = new SqlConnection(masterCatalogConnectionString))
                {
                    conn.Open();
                    using (var command = new SqlCommand(query, conn))
                    {
                        command.ExecuteNonQuery();  
                    } 
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                return string.Format(_locService.GetResource("DatabaseCreationError"), ex.Message);
            }
        }
        
        /// <summary>
        /// Create contents of connection strings used by the SqlConnection class
        /// </summary>
        /// <param name="trustedConnection">Avalue that indicates whether User ID and Password are specified in the connection (when false) or whether the current Windows account credentials are used for authentication (when true)</param>
        /// <param name="serverName">The name or network address of the instance of SQL Server to connect to</param>
        /// <param name="databaseName">The name of the database associated with the connection</param>
        /// <param name="userName">The user ID to be used when connecting to SQL Server</param>
        /// <param name="password">The password for the SQL Server account</param>
        /// <param name="timeout">The connection timeout</param>
        /// <returns>Connection string</returns>
        [NonAction]
        protected string CreateConnectionString(bool trustedConnection,
            string serverName, string databaseName, 
            string userName, string password, int timeout = 15 /* codehint: sm-edit (was 0) */)
        {
            var builder = new SqlConnectionStringBuilder();
            builder.IntegratedSecurity = trustedConnection;
            builder.DataSource = serverName;
            builder.InitialCatalog = databaseName;
            if (!trustedConnection)
            {
                builder.UserID = userName;
                builder.Password = password;
            }
            builder.PersistSecurityInfo = false;
            builder.MultipleActiveResultSets = true;

            // codehint: sm-add
            builder.UserInstance = false;
            builder.Pooling = true;
            builder.MinPoolSize = 1;
            builder.MaxPoolSize = 100;

            if (timeout > 0)
            {
                builder.ConnectTimeout = timeout;
            }
            return builder.ConnectionString;
        }

        #endregion

        #region Methods

        public ActionResult Index()
        {
            if (DataSettingsHelper.DatabaseIsInstalled())
                return RedirectToRoute("HomePage");

            //set page timeout to 5 minutes
            this.Server.ScriptTimeout = 300;

            var model = new InstallModel()
            {
                AdminEmail = _locService.GetResource("AdminEmailValue"),
                //AdminPassword = "admin",
                //ConfirmPassword = "admin",
                InstallSampleData = false,
                DatabaseConnectionString = "",
                DataProvider = "sqlce", // "sqlserver",
                SqlAuthenticationType = "sqlauthentication",
                SqlConnectionInfo = "sqlconnectioninfo_values",
                SqlServerCreateDatabase = false,
                UseCustomCollation = false,
                Collation = "SQL_Latin1_General_CP1_CI_AS",
            };

            foreach (var lang in _locService.GetAvailableLanguages())
            {
                model.AvailableLanguages.Add(new SelectListItem()
                {
                    Value = Url.Action("ChangeLanguage", "Install", new { language = lang.Code}),
                    Text = lang.Name,
                    Selected = _locService.GetCurrentLanguage().Code == lang.Code,
                });
            }
            
            foreach (var lang in _locService.GetAvailableAppLanguages())
            {
                model.AvailableAppLanguages.Add(new SelectListItem()
                {
                    Value = lang.Culture,
                    Text = lang.Name,
                    Selected = lang.Culture == Thread.CurrentThread.CurrentCulture.IetfLanguageTag // TODO (?)
                });
            }

            model.AvailableMediaStorages.Add(new SelectListItem { Value = "db", Text = _locService.GetResource("MediaStorage.DB"), Selected = true });
            model.AvailableMediaStorages.Add(new SelectListItem { Value = "fs", Text = _locService.GetResource("MediaStorage.FS") });

            return View(model);
        }
        
        [HttpPost]
        public JsonResult Progress()
        {
            return Json(GetInstallResult());
        }

        [HttpPost]
        public async Task<JsonResult> Install(InstallModel model)
        {
            //var result = await InstallCore(model);
            var result = await Task.Run<InstallationResult>(() => InstallCore(model));
            return Json(result);
        }

        [NonAction]
        protected virtual InstallationResult InstallCore(InstallModel model)
        {
            UpdateResult(x => {
                x.Progress = 0;
                x.Completed = false;       
            });

            if (DataSettingsHelper.DatabaseIsInstalled())
            {
                return UpdateResult(x =>
                {
                    x.Success = true;
                    x.RedirectUrl = Url.RouteUrl("HomePage");
                });
            }

            //set page timeout to 5 minutes
            this.Server.ScriptTimeout = 300;

            if (model.DatabaseConnectionString != null)
            {
                model.DatabaseConnectionString = model.DatabaseConnectionString.Trim();
            }

            //SQL Server
            if (model.DataProvider.Equals("sqlserver", StringComparison.InvariantCultureIgnoreCase))
            {
                if (model.SqlConnectionInfo.Equals("sqlconnectioninfo_raw", StringComparison.InvariantCultureIgnoreCase))
                {
                    //raw connection string
                    if (string.IsNullOrEmpty(model.DatabaseConnectionString))
                    { 
                        UpdateResult(x =>
                        {
                            x.Errors.Add(_locService.GetResource("ConnectionStringRequired"));
                        });
                    }

                    try
                    {
                        //try to create connection string
                        new SqlConnectionStringBuilder(model.DatabaseConnectionString);
                    }
                    catch
                    {
                        UpdateResult(x =>
                        {
                            x.Errors.Add(_locService.GetResource("ConnectionStringWrongFormat"));
                        });
                    }
                }
                else
                {
                    //values
                    if (string.IsNullOrEmpty(model.SqlServerName))
                    {
                        UpdateResult(x =>
                        {
                            x.Errors.Add(_locService.GetResource("SqlServerNameRequired"));
                        });
                    }

                    if (string.IsNullOrEmpty(model.SqlDatabaseName))
                    {
                        UpdateResult(x =>
                        {
                            x.Errors.Add(_locService.GetResource("DatabaseNameRequired"));
                        });
                    }

                    //authentication type
                    if (model.SqlAuthenticationType.Equals("sqlauthentication", StringComparison.InvariantCultureIgnoreCase))
                    {
                        //SQL authentication
                        if (string.IsNullOrEmpty(model.SqlServerUsername))
                        {
                            UpdateResult(x =>
                            {
                                x.Errors.Add(_locService.GetResource("SqlServerUsernameRequired"));
                            });
                        }

                        if (string.IsNullOrEmpty(model.SqlServerPassword))
                        {
                            UpdateResult(x =>
                            {
                                x.Errors.Add(_locService.GetResource("SqlServerPasswordRequired"));
                            });
                        }
                    }
                }
            }


            //Consider granting access rights to the resource to the ASP.NET request identity. 
            //ASP.NET has a base process identity 
            //(typically {MACHINE}\ASPNET on IIS 5 or Network Service on IIS 6 and IIS 7, 
            //and the configured application pool identity on IIS 7.5) that is used if the application is not impersonating.
            //If the application is impersonating via <identity impersonate="true"/>, 
            //the identity will be the anonymous user (typically IUSR_MACHINENAME) or the authenticated request user.
            var webHelper = EngineContext.Current.Resolve<IWebHelper>();
            //validate permissions
            var dirsToCheck = FilePermissionHelper.GetDirectoriesWrite(webHelper);
            foreach (string dir in dirsToCheck)
            {
                if (!FilePermissionHelper.CheckPermissions(dir, false, true, true, false))
                {
                    UpdateResult(x =>
                    {
                        x.Errors.Add(string.Format(_locService.GetResource("ConfigureDirectoryPermissions"), WindowsIdentity.GetCurrent().Name, dir));
                    });
                }
            }

            var filesToCheck = FilePermissionHelper.GetFilesWrite(webHelper);
            foreach (string file in filesToCheck)
            {
                if (!FilePermissionHelper.CheckPermissions(file, false, true, true, true))
                {
                    UpdateResult(x =>
                    {
                        x.Errors.Add(string.Format(_locService.GetResource("ConfigureFilePermissions"), WindowsIdentity.GetCurrent().Name, file));
                    });
                }
            }

            if (GetInstallResult().HasErrors) 
            {
                return UpdateResult(x =>
                {
                    x.Completed = true;
                    x.Success = false;
                    x.RedirectUrl = null;
                });
            }
            else 
            {
                var settingsManager = new DataSettingsManager();
                try
                {
                    string connectionString = null;
                    if (model.DataProvider.Equals("sqlserver", StringComparison.InvariantCultureIgnoreCase))
                    {
                        //SQL Server

                        if (model.SqlConnectionInfo.Equals("sqlconnectioninfo_raw", StringComparison.InvariantCultureIgnoreCase))
                        {
                            //raw connection string

                            //we know that MARS option is required when using Entity Framework
                            //let's ensure that it's specified
                            var sqlCsb = new SqlConnectionStringBuilder(model.DatabaseConnectionString);
                            sqlCsb.MultipleActiveResultSets = true;
                            connectionString = sqlCsb.ToString();
                        }
                        else
                        {
                            //values
                            connectionString = CreateConnectionString(model.SqlAuthenticationType == "windowsauthentication",
                                model.SqlServerName, model.SqlDatabaseName,
                                model.SqlServerUsername, model.SqlServerPassword);
                        }

                        if (model.SqlServerCreateDatabase)
                        {
                            if (!SqlServerDatabaseExists(connectionString))
                            {
                                //create database
                                var collation = model.UseCustomCollation ? model.Collation : "";
                                var errorCreatingDatabase = CreateDatabase(connectionString, collation);
                                if (!String.IsNullOrEmpty(errorCreatingDatabase))
                                {
                                    return UpdateResult(x =>
                                    {
                                        x.Errors.Add(errorCreatingDatabase);
                                        x.Completed = true;
                                        x.Success = false;
                                        x.RedirectUrl = null;
                                    });
                                }
                                else
                                {
                                    //Database cannot be created sometimes. Weird! Seems to be Entity Framework issue
                                    //that's just wait 3 seconds
                                    Thread.Sleep(3000);
                                }
                            }
                        }
                        else
                        {
                            //check whether database exists
                            if (!SqlServerDatabaseExists(connectionString))
                            {
                                return UpdateResult(x =>
                                {
                                    x.Errors.Add(_locService.GetResource("DatabaseNotExists"));
                                    x.Completed = true;
                                    x.Success = false;
                                    x.RedirectUrl = null;
                                });
                            }
                        }
                    }
                    else
                    {
                        //SQL CE
                        string databaseFileName = "SmartStore.Db.sdf";
                        string databasePath = @"|DataDirectory|\" + databaseFileName;
                        connectionString = "Data Source=" + databasePath + ";Persist Security Info=False";

                        //drop database if exists
                        string databaseFullPath = HostingEnvironment.MapPath("~/App_Data/") + databaseFileName;
                        if (System.IO.File.Exists(databaseFullPath))
                        {
                            System.IO.File.Delete(databaseFullPath);
                        }
                    }

                    //save settings
                    var dataProvider = model.DataProvider;
                    var settings = new DataSettings()
                    {
                        DataProvider = dataProvider,
                        DataConnectionString = connectionString
                    };
                    settingsManager.SaveSettings(settings);

                    //init data provider
                    var dataProviderInstance = EngineContext.Current.Resolve<BaseDataProviderManager>().LoadDataProvider();
                    dataProviderInstance.InitDatabase();

                    //now resolve installation service
                    var installationService = EngineContext.Current.Resolve<IInstallationService>();

                    // codehint: sm-add
                    // resolve installdata instance from primary language
                    var lazyLang = _locService.GetAppLanguage(model.PrimaryLanguage);
                    if (lazyLang == null)
                    {
                        return UpdateResult(x =>
                        {
                            x.Errors.Add(string.Format("The install language '{0}' is not registered", model.PrimaryLanguage));
                            x.Completed = true;
                            x.Success = false;
                            x.RedirectUrl = null;
                        });
                    }

                    // codehint: sm-add
                    // create Language Object from lazyLang
                    var rsLanguages = EngineContext.Current.Resolve<IRepository<Language>>();
                    // create a proxied type, resources cannot be saved otherwise
                    var primaryLanguage = rsLanguages.Create();
                    primaryLanguage.Name = lazyLang.Metadata.Name;
                    primaryLanguage.LanguageCulture = lazyLang.Metadata.Culture;
                    primaryLanguage.UniqueSeoCode = lazyLang.Metadata.UniqueSeoCode;
                    primaryLanguage.FlagImageFileName = lazyLang.Metadata.FlagImageFileName;

                    var installContext = new InstallDataContext 
                    {
                        DefaultUserName = model.AdminEmail,
                        DefaultUserPassword = model.AdminPassword,
                        InstallSampleData = model.InstallSampleData,
                        InstallData = lazyLang.Value,
                        Language = primaryLanguage,
                        StoreMediaInDB = model.MediaStorage == "db",
                        ProgressCallback = p => UpdateResult(x => x.Progress = (p / 2))
                    };
                    installationService.InstallData(installContext);

                    //reset cache
                    //DataSettingsHelper.ResetCache();

                    //install plugins
                    PluginManager.MarkAllPluginsAsUninstalled();
                    var pluginFinder = EngineContext.Current.Resolve<IPluginFinder>();
                    var plugins = pluginFinder.GetPlugins<IPlugin>(false)
                        //.ToList()
                        .OrderBy(x => x.PluginDescriptor.Group)
                        .ThenBy(x => x.PluginDescriptor.DisplayOrder)
                        .ToList();

                    var pluginsIgnoredDuringInstallation = String.IsNullOrEmpty(ConfigurationManager.AppSettings["PluginsIgnoredDuringInstallation"]) ? 
	                    new List<string>(): 
	                    ConfigurationManager.AppSettings["PluginsIgnoredDuringInstallation"]
	                        .Split(new char[] {','}, StringSplitOptions.RemoveEmptyEntries)
	                        .Select(x => x.Trim())
                        .ToList();

                    if (pluginsIgnoredDuringInstallation.Count > 0)
                    {
                        plugins = plugins.Where(x => !pluginsIgnoredDuringInstallation.Contains(x.PluginDescriptor.SystemName, StringComparer.OrdinalIgnoreCase)).ToList();
                    }

                    _totalSteps = plugins.Count;
                    _currentStep = 0;

                    foreach (var plugin in plugins)
                    {
                        // codehint: sm-edit
                        try
                        {
                            plugin.Install();
                        }
                        catch (Exception ex)
                        {
                            if (plugin.PluginDescriptor.Installed)
                            {
                                PluginManager.MarkPluginAsUninstalled(plugin.PluginDescriptor.SystemName);
                            }
                            System.Diagnostics.Debug.Write(ex.Message);
                        }
                        finally
                        {
                            IncreaseProgress();
                        }
                    }

                    //register default permissions
                    //var permissionProviders = EngineContext.Current.Resolve<ITypeFinder>().FindClassesOfType<IPermissionProvider>();
                    var permissionProviders = new List<Type>();
                    permissionProviders.Add(typeof(StandardPermissionProvider));
                    foreach (var providerType in permissionProviders)
                    {
                        dynamic provider = Activator.CreateInstance(providerType);
                        EngineContext.Current.Resolve<IPermissionService>().InstallPermissions(provider);
                    }
                    _currentStep = _totalSteps;

                    ////restart application
                    //webHelper.RestartAppDomain();

                    // SUCCESS: Redirect to home page
                    return UpdateResult(x =>
                    {
                        x.Completed = true;
                        x.Success = true;
                        x.RedirectUrl = Url.RouteUrl("HomePage");
                    });
                }
                catch (Exception exception)
                {
                    //reset cache
                    //DataSettingsHelper.ResetCache();

                    //clear provider settings if something got wrong
                    settingsManager.SaveSettings(new DataSettings
                    {
                        DataProvider = null,
                        DataConnectionString = null
                    });

                    var msg = exception.Message;
                    if (exception.InnerException != null)
                    {
                        msg += " (" + exception.InnerException.Message + ")";
                    }

                    return UpdateResult(x =>
                    {
                        x.Errors.Add(string.Format(_locService.GetResource("SetupFailed"), msg));
                        x.Success = false;
                        x.Completed = true;
                        x.RedirectUrl = null;
                    });
                }
            }
        }

        [HttpPost]
        public ActionResult Finalize(bool restart)
        {
            HttpRuntime.Cache.Remove(InstallResultStorageKey);
            HttpRuntime.Cache.Remove(InstallLockStorageKey);

            if (restart)
            {
                var webHelper = EngineContext.Current.Resolve<IWebHelper>();
                webHelper.RestartAppDomain();
            }

            return Json(new { Success = true });
        }

        #region OBSOLETE

        //[HttpPost]
        //public ActionResult Index(InstallModel model)
        //{
        //    if (DataSettingsHelper.DatabaseIsInstalled())
        //        return RedirectToRoute("HomePage");

        //    //set page timeout to 5 minutes
        //    this.Server.ScriptTimeout = 300;

        //    if (model.DatabaseConnectionString != null)
        //        model.DatabaseConnectionString = model.DatabaseConnectionString.Trim();

        //    //prepare language list
        //    foreach (var lang in _locService.GetAvailableLanguages())
        //    {
        //        model.AvailableLanguages.Add(new SelectListItem()
        //        {
        //            Value = Url.Action("ChangeLanguage", "Install", new { language = lang.Code }),
        //            Text = lang.Name,
        //            Selected = _locService.GetCurrentLanguage().Code == lang.Code,
        //        });
        //    }

        //    //SQL Server
        //    if (model.DataProvider.Equals("sqlserver", StringComparison.InvariantCultureIgnoreCase))
        //    {
        //        if (model.SqlConnectionInfo.Equals("sqlconnectioninfo_raw", StringComparison.InvariantCultureIgnoreCase))
        //        {
        //            //raw connection string
        //            if (string.IsNullOrEmpty(model.DatabaseConnectionString))
        //                ModelState.AddModelError("", _locService.GetResource("ConnectionStringRequired"));

        //            try
        //            {
        //                //try to create connection string
        //                new SqlConnectionStringBuilder(model.DatabaseConnectionString);
        //            }
        //            catch
        //            {
        //                ModelState.AddModelError("", _locService.GetResource("ConnectionStringWrongFormat"));
        //            }
        //        }
        //        else
        //        {
        //            //values
        //            if (string.IsNullOrEmpty(model.SqlServerName))
        //                ModelState.AddModelError("", _locService.GetResource("SqlServerNameRequired"));
        //            if (string.IsNullOrEmpty(model.SqlDatabaseName))
        //                ModelState.AddModelError("", _locService.GetResource("DatabaseNameRequired"));

        //            //authentication type
        //            if (model.SqlAuthenticationType.Equals("sqlauthentication", StringComparison.InvariantCultureIgnoreCase))
        //            {
        //                //SQL authentication
        //                if (string.IsNullOrEmpty(model.SqlServerUsername))
        //                    ModelState.AddModelError("", _locService.GetResource("SqlServerUsernameRequired"));
        //                if (string.IsNullOrEmpty(model.SqlServerPassword))
        //                    ModelState.AddModelError("", _locService.GetResource("SqlServerPasswordRequired"));
        //            }
        //        }
        //    }


        //    //Consider granting access rights to the resource to the ASP.NET request identity. 
        //    //ASP.NET has a base process identity 
        //    //(typically {MACHINE}\ASPNET on IIS 5 or Network Service on IIS 6 and IIS 7, 
        //    //and the configured application pool identity on IIS 7.5) that is used if the application is not impersonating.
        //    //If the application is impersonating via <identity impersonate="true"/>, 
        //    //the identity will be the anonymous user (typically IUSR_MACHINENAME) or the authenticated request user.
        //    var webHelper = EngineContext.Current.Resolve<IWebHelper>();
        //    //validate permissions
        //    var dirsToCheck = FilePermissionHelper.GetDirectoriesWrite(webHelper);
        //    foreach (string dir in dirsToCheck)
        //        if (!FilePermissionHelper.CheckPermissions(dir, false, true, true, false))
        //            ModelState.AddModelError("", string.Format(_locService.GetResource("ConfigureDirectoryPermissions"), WindowsIdentity.GetCurrent().Name, dir));

        //    var filesToCheck = FilePermissionHelper.GetFilesWrite(webHelper);
        //    foreach (string file in filesToCheck)
        //        if (!FilePermissionHelper.CheckPermissions(file, false, true, true, true))
        //            ModelState.AddModelError("", string.Format(_locService.GetResource("ConfigureFilePermissions"), WindowsIdentity.GetCurrent().Name, file));
            
        //    if (ModelState.IsValid)
        //    {
        //        var settingsManager = new DataSettingsManager();
        //        try
        //        {
        //            string connectionString = null;
        //            if (model.DataProvider.Equals("sqlserver", StringComparison.InvariantCultureIgnoreCase))
        //            {
        //                //SQL Server

        //                if (model.SqlConnectionInfo.Equals("sqlconnectioninfo_raw", StringComparison.InvariantCultureIgnoreCase))
        //                {
        //                    //raw connection string

        //                    //we know that MARS option is required when using Entity Framework
        //                    //let's ensure that it's specified
        //                    var sqlCsb = new SqlConnectionStringBuilder(model.DatabaseConnectionString);
        //                    sqlCsb.MultipleActiveResultSets = true;
        //                    connectionString = sqlCsb.ToString();
        //                }
        //                else
        //                {
        //                    //values
        //                    connectionString = CreateConnectionString(model.SqlAuthenticationType == "windowsauthentication",
        //                        model.SqlServerName, model.SqlDatabaseName,
        //                        model.SqlServerUsername, model.SqlServerPassword);
        //                }
                        
        //                if (model.SqlServerCreateDatabase)
        //                {
        //                    if (!SqlServerDatabaseExists(connectionString))
        //                    {
        //                        //create database
        //                        var collation = model.UseCustomCollation ? model.Collation : "";
        //                        var errorCreatingDatabase = CreateDatabase(connectionString, collation);
        //                        if (!String.IsNullOrEmpty(errorCreatingDatabase))
        //                            throw new Exception(errorCreatingDatabase);
        //                        else
        //                        {
        //                            //Database cannot be created sometimes. Weird! Seems to be Entity Framework issue
        //                            //that's just wait 3 seconds
        //                            Thread.Sleep(3000);
        //                        }
        //                    }
        //                }
        //                else
        //                {
        //                    //check whether database exists
        //                    if (!SqlServerDatabaseExists(connectionString))
        //                        throw new Exception(_locService.GetResource("DatabaseNotExists"));
        //                }
        //            }
        //            else
        //            {
        //                //SQL CE
        //                string databaseFileName = "SmartStore.Db.sdf";
        //                string databasePath = @"|DataDirectory|\" + databaseFileName;
        //                connectionString = "Data Source=" + databasePath + ";Persist Security Info=False";

        //                //drop database if exists
        //                string databaseFullPath = HostingEnvironment.MapPath("~/App_Data/") + databaseFileName;
        //                if (System.IO.File.Exists(databaseFullPath))
        //                {
        //                    System.IO.File.Delete(databaseFullPath);
        //                }
        //            }

        //            //save settings
        //            var dataProvider = model.DataProvider;
        //            var settings = new DataSettings()
        //            {
        //                DataProvider = dataProvider,
        //                DataConnectionString = connectionString
        //            };
        //            settingsManager.SaveSettings(settings);

        //            //init data provider
        //            var dataProviderInstance = EngineContext.Current.Resolve<BaseDataProviderManager>().LoadDataProvider();
        //            dataProviderInstance.InitDatabase();
                    
                    
        //            //now resolve installation service
        //            var installationService = EngineContext.Current.Resolve<IInstallationService>();
        //            // Temp
        //            var primaryLanguage = new Language
        //            {
        //                Name = "English",
        //                LanguageCulture = "en-US",
        //                UniqueSeoCode = "en",
        //                FlagImageFileName = "us.png"
        //            };
        //            installationService.InstallData(model.AdminEmail, model.AdminPassword, primaryLanguage, new EnUSInstallationData(), model.InstallSampleData);

        //            //reset cache
        //            DataSettingsHelper.ResetCache();

        //            //install plugins
        //            PluginManager.MarkAllPluginsAsUninstalled();
        //            var pluginFinder = EngineContext.Current.Resolve<IPluginFinder>();
        //            var plugins = pluginFinder.GetPlugins<IPlugin>(false)
        //                //.ToList()
        //                .OrderBy(x => x.PluginDescriptor.Group)
        //                .ThenBy(x => x.PluginDescriptor.DisplayOrder)
        //                .ToList();
        //            foreach (var plugin in plugins)
        //            {
        //                // codehint: sm-edit
        //                try
        //                {
        //                    plugin.Install();
        //                }
        //                catch
        //                {
        //                    if (plugin.PluginDescriptor.Installed)
        //                    {
        //                        PluginManager.MarkPluginAsUninstalled(plugin.PluginDescriptor.SystemName);
        //                    }
        //                }
        //            }
                    
        //            //register default permissions
        //            //var permissionProviders = EngineContext.Current.Resolve<ITypeFinder>().FindClassesOfType<IPermissionProvider>();
        //            var permissionProviders = new List<Type>();
        //            permissionProviders.Add(typeof(StandardPermissionProvider));
        //            foreach (var providerType in permissionProviders)
        //            {
        //                dynamic provider = Activator.CreateInstance(providerType);
        //                EngineContext.Current.Resolve<IPermissionService>().InstallPermissions(provider);
        //            }

        //            //restart application
        //            webHelper.RestartAppDomain();

        //            //Redirect to home page
        //            return RedirectToRoute("HomePage");
        //        }
        //        catch (Exception exception)
        //        {
        //            //reset cache
        //            DataSettingsHelper.ResetCache();

        //            //clear provider settings if something got wrong
        //            settingsManager.SaveSettings(new DataSettings
        //            {
        //                DataProvider = null,
        //                DataConnectionString = null
        //            });

        //            // codehint: sm-add
        //            var msg = exception.Message;
        //            if (exception.InnerException != null)
        //            {
        //                msg += " (" + exception.InnerException.Message + ")";
        //            }

        //            ModelState.AddModelError("", string.Format(_locService.GetResource("SetupFailed"), msg));
        //        }
        //    }
        //    return View(model);
        //}

        #endregion

        public ActionResult ChangeLanguage(string language)
        {
            if (DataSettingsHelper.DatabaseIsInstalled())
                return RedirectToRoute("HomePage");

            _locService.SaveCurrentLanguage(language);

            //Reload the page);
            return RedirectToAction("Index", "Install");
        }

        public ActionResult RestartInstall()
        {
            if (DataSettingsHelper.DatabaseIsInstalled())
                return RedirectToRoute("HomePage");
            
            //restart application
            var webHelper = EngineContext.Current.Resolve<IWebHelper>();
            webHelper.RestartAppDomain();

            //Redirect to home page
            return RedirectToRoute("HomePage");
        }

        #endregion
    }

    public class InstallationResult
    {
        private int _progress = -1;

        public InstallationResult()
        {
            this.Errors = new List<string>();
        }

        public int Progress
        {
            get { return Math.Min(100, _progress); }
            set { _progress = value; }
        }

        public bool Completed { get; set; }
        public bool Success { get; set; }
        public string RedirectUrl { get; set; }
        public IList<string> Errors { get; private set; }
        public bool HasErrors
        {
            get { return this.Errors.Count > 0; }
        }
    }
}
