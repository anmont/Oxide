using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using V8.Net;

using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Plugins.Watchers;

namespace Oxide.Ext.JavaScript.Plugins
{
    /// <summary>
    /// Represents a JavaScript plugin
    /// </summary>
    public class JavaScriptPlugin : Plugin
    {
        /// <summary>
        /// Gets the JavaScript Engine
        /// </summary>
        public V8Engine JavaScriptEngine { get; private set; }

        /// <summary>
        /// Gets this plugin's JavaScript Class
        /// </summary>
        public ObjectHandle Class { get; private set; }

        /// <summary>
        /// Gets the object associated with this plugin
        /// </summary>
        public override object Object { get { return Class; } }

        /// <summary>
        /// Gets the filename of this plugin
        /// </summary>
        public string Filename { get; private set; }

        public IList<string> Globals;

        // The plugin change watcher
        private readonly FSWatcher watcher;

        private readonly List<V8Function> funcs;

        /// <summary>
        /// Initializes a new instance of the JavaScriptPlugin class
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="v8Engine"></param>
        /// <param name="watcher"></param>
        internal JavaScriptPlugin(string filename, V8Engine v8Engine, FSWatcher watcher)
        {
            // Store filename
            Filename = filename;
            JavaScriptEngine = v8Engine;
            this.watcher = watcher;
            funcs = new List<V8Function>();
        }

        #region Config

        /// <summary>
        /// Populates the config with default settings
        /// </summary>
        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            if (Class != null)
            {
                Class.SetProperty("Config", JavaScriptEngine.CreateObject());
            }
            CallHook("LoadDefaultConfig", null);
        }

        /// <summary>
        /// Loads the config file for this plugin
        /// </summary>
        protected override void LoadConfig()
        {
            base.LoadConfig();
            if (Class != null)
            {
                Class.SetProperty("Config", Utility.ObjectFromConfig(Config, JavaScriptEngine));
            }
        }

        /// <summary>
        /// Saves the config file for this plugin
        /// </summary>
        protected override void SaveConfig()
        {
            if (Config == null) return;
            if (Class == null) return;
            Utility.SetConfigFromObject(Config, Class.GetProperty("Config"));
            base.SaveConfig();
        }

        #endregion

        /// <summary>
        /// Loads this plugin
        /// </summary>
        public void Load()
        {
            // Load the plugin
            string code = File.ReadAllText(Filename);
            Name = Path.GetFileNameWithoutExtension(Filename);
            var compiled = JavaScriptEngine.Compile(code, Filename, true);
            JavaScriptEngine.Execute(compiled, true);
            if (JavaScriptEngine.GlobalObject.GetProperty(Name).ValueType == JSValueType.Undefined) throw new Exception("Plugin is missing main object");
            Class = JavaScriptEngine.GlobalObject.GetProperty(Name);
            Class.SetProperty("Name", JavaScriptEngine.CreateValue(Name));
            // Read plugin attributes
            if (Class.GetProperty("Title").ValueType != JSValueType.String) throw new Exception("Plugin is missing title");
            if (Class.GetProperty("Author").ValueType != JSValueType.String) throw new Exception("Plugin is missing author");
            if (Class.GetProperty("Version").ValueType != JSValueType.Object) throw new Exception("Plugin is missing version");
            Title = Class.GetProperty("Title").AsString;
            Author = Class.GetProperty("Author").AsString;
            Version = Class.GetProperty("Version").As<VersionNumber>();
            if (Class.GetProperty("ResourceId")) ResourceId = (int)Class.Get("ResourceId").AsNumber();
            HasConfig = Class.GetProperty("HasConfig").ValueType == JSValueType.Bool && Class.GetProperty("HasConfig").AsBoolean;

            // Set attributes
            Class.SetProperty("Plugin", JavaScriptEngine.CreateValue(this));

            Globals = new List<string>();
            foreach (var name in Class.GetPropertyNames())
            {
                if (Class.GetProperty(name).ValueType == JSValueType.Function)
                {
                    Globals.Add(name);
                }
            }

            // Bind any base methods (we do it here because we don't want them to be hooked)
            BindBaseMethods();
        }

        /// <summary>
        /// Binds base methods
        /// </summary>
        private void BindBaseMethods()
        {
            BindBaseMethod("SaveConfig", "SaveConfig");
        }

        /// <summary>
        /// Binds the specified base method
        /// </summary>
        /// <param name="methodname"></param>
        /// <param name="name"></param>
        private void BindBaseMethod(string methodname, string name)
        {
            MethodInfo method = GetType().GetMethod(methodname, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
            var expectedParameters = method.GetParameters();
            var template = JavaScriptEngine.CreateFunctionTemplate(name);
            var plugin = this;
            var convertedArguments = new object[expectedParameters.Length];
            var func = template.GetFunctionObject((engine, call, @this, args) =>
            {
                var argInfos = ArgInfo.GetArguments(args, 0, expectedParameters);
                for (var paramIndex = 0; paramIndex < argInfos.Length; paramIndex++)
                {
                    var tInfo = argInfos[paramIndex];
                    if (tInfo.HasError) throw tInfo.Error;
                    convertedArguments[paramIndex] = tInfo.ValueOrDefault;
                }
                var result = method.Invoke(plugin, convertedArguments);
                return method.ReturnType == typeof(void) ? InternalHandle.Empty : JavaScriptEngine.CreateValue(result, true);
            });
            Class.SetProperty(name, func);
            funcs.Add(func);
        }

        /// <summary>
        /// Called when this plugin has been added to the specified manager
        /// </summary>
        /// <param name="manager"></param>
        public override void HandleAddedToManager(PluginManager manager)
        {
            // Call base
            base.HandleAddedToManager(manager);

            // Subscribe all our hooks
            foreach (string key in Globals)
                Subscribe(key);

            // Add us to the watcher
            watcher.AddMapping(Name);

            // Let the plugin know that it's loading
            CallFunction("Init", null);
        }

        /// <summary>
        /// Called when this plugin has been removed from the specified manager
        /// </summary>
        /// <param name="manager"></param>
        public override void HandleRemovedFromManager(PluginManager manager)
        {
            // Let plugin know that it's unloading
            CallFunction("Unload", null);

            // Remove us from the watcher
            watcher.RemoveMapping(Name);

            // Call base
            base.HandleRemovedFromManager(manager);
        }

        /// <summary>
        /// Called when it's time to call a hook
        /// </summary>
        /// <param name="hookname"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        protected override object OnCallHook(string hookname, object[] args)
        {
            // Call it
            return CallFunction(hookname, args);
        }

        /// <summary>
        /// Calls a function by the given name and returns the output
        /// </summary>
        /// <param name="name"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private object CallFunction(string name, object[] args)
        {
            //Manager.Logger.Write(LogType.Info, "Call: " + name);
            if (!Globals.Contains(name) || Class.GetProperty(name).ValueType != JSValueType.Function) return null;
            return ((ObjectHandle)Class.GetProperty(name)).Call(Class, args != null ? args.Select(x => JavaScriptEngine.CreateValue(x)).ToArray() : new InternalHandle[] { });
        }
    }
}
