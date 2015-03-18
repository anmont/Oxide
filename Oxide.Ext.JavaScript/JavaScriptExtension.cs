using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using V8.Net;

using Oxide.Core;
using Oxide.Core.Extensions;
using Oxide.Core.Libraries;
using Oxide.Core.Logging;
using Oxide.Core.Plugins.Watchers;

using Oxide.Ext.JavaScript.Libraries;
using Oxide.Ext.JavaScript.Plugins;

namespace Oxide.Ext.JavaScript
{
    /// <summary>
    /// The extension class that represents this extension
    /// </summary>
    public class JavaScriptExtension : Extension
    {
        /// <summary>
        /// Gets the name of this extension
        /// </summary>
        public override string Name { get { return "JavaScript"; } }

        /// <summary>
        /// Gets the version of this extension
        /// </summary>
        public override VersionNumber Version { get { return new VersionNumber(1, 0, OxideMod.Version.Patch); } }

        /// <summary>
        /// Gets the author of this extension
        /// </summary>
        public override string Author { get { return "Nogrod"; } }

        /// <summary>
        /// Gets the JavaScript engine
        /// </summary>
        public V8Engine JavaScriptEngine { get; private set; }

        // The plugin change watcher
        private FSWatcher watcher;

        // The plugin loader
        private JavaScriptPluginLoader loader;
        private readonly List<V8Function> funcs;

        // Whitelists
        private static readonly string[] WhitelistAssemblies = { "Assembly-CSharp", "DestMath", "mscorlib", "Oxide.Core", "protobuf-net", "RustBuild", "System", "System.Core", "UnityEngine" };
        private static readonly string[] WhitelistNamespaces = { "Dest", "Facepunch", "Network", "ProtoBuf", "PVT", "Rust", "Steamworks", "System.Collections", "UnityEngine" };

        /// <summary>
        /// Initializes a new instance of the JavaScript class
        /// </summary>
        /// <param name="manager"></param>
        public JavaScriptExtension(ExtensionManager manager)
            : base(manager)
        {
            funcs = new List<V8Function>();
        }

        /// <summary>
        /// Loads this extension
        /// </summary>
        public override void Load()
        {
            // Setup JavaScript instance
            InitializeJavaScript();

            // Register the loader
            loader = new JavaScriptPluginLoader(JavaScriptEngine);
            Manager.RegisterPluginLoader(loader);
        }

        /// <summary>
        /// Initializes the JavaScript engine
        /// </summary>
        private void InitializeJavaScript()
        {
            // Create the JavaScript engine
            JavaScriptEngine = new V8Engine();
            // Bind all namespaces and types
            foreach (var type in AppDomain.CurrentDomain.GetAssemblies()
                .Where(AllowAssemblyAccess)
                .SelectMany(Utility.GetAllTypesFromAssembly)
                .Where(AllowTypeAccess))
            {
                // Get the namespace object
                ObjectHandle namespaceObject = GetNamespaceObject(Utility.GetNamespace(type));
                // Bind the type
                JavaScriptEngine.RegisterType(type, null, true, ScriptMemberSecurity.Locked);
                namespaceObject.SetProperty(type);
            }
        }

        /// <summary>
        /// Gets the namespace object for the specified namespace
        /// </summary>
        /// <param name="nspace"></param>
        /// <returns></returns>
        private ObjectHandle GetNamespaceObject(string nspace)
        {
            if (string.IsNullOrEmpty(nspace))
            {
                return JavaScriptEngine.GlobalObject;
            }
            string[] nspacesplit = nspace.Split('.');
            ObjectHandle curObject = JavaScriptEngine.GlobalObject;
            foreach (string t in nspacesplit)
            {
                ObjectHandle prevObject = curObject;
                if (prevObject.GetProperty(t).ValueType != JSValueType.Object)
                {
                    curObject = JavaScriptEngine.CreateObject();
                    prevObject.SetProperty(t, curObject);
                }
                curObject = prevObject.GetProperty(t);
            }
            return curObject;
        }

        /// <summary>
        /// Returns if the specified assembly should be loaded or not
        /// </summary>
        /// <param name="assembly"></param>
        /// <returns></returns>
        internal bool AllowAssemblyAccess(Assembly assembly)
        {
            return WhitelistAssemblies.Any(whitelist => assembly.GetName().Name.Equals(whitelist));
        }

        /// <summary>
        /// Returns if the specified type should be bound to JavaScript or not
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        internal bool AllowTypeAccess(Type type)
        {
            // Special case: allow access to Oxide.Core.OxideMod
            if (type.FullName == "Oxide.Core.OxideMod") return true;
            // The only exception is to allow all value types directly under System
            string nspace = Utility.GetNamespace(type);
            if (string.IsNullOrEmpty(nspace)) return true;
            if (nspace == "System" && (type.IsValueType || type.Name == "String")) return true;
            foreach (string whitelist in WhitelistNamespaces)
                if (nspace.StartsWith(whitelist)) return true;
            return false;
        }

        /// <summary>
        /// Loads a library into the specified path
        /// </summary>
        /// <param name="library"></param>
        /// <param name="path"></param>
        public void LoadLibrary(Library library, string path)
        {
            ObjectHandle scope;
            if (library.IsGlobal)
            {
                scope = JavaScriptEngine.GlobalObject;
            }
            else if (JavaScriptEngine.GlobalObject.GetProperty(path).ValueType == JSValueType.Undefined)
            {
                scope = JavaScriptEngine.CreateObject();
                JavaScriptEngine.GlobalObject.SetProperty(path, scope);
            }
            else
            {
                scope = JavaScriptEngine.GlobalObject.GetProperty(path);
            }
            if (scope == null)
            {
                Manager.Logger.Write(LogType.Info, "Library path: " + path + " cannot be set");
                return;
            }
            foreach (string name in library.GetFunctionNames())
            {
                MethodInfo method = library.GetFunction(name);
                var expectedParameters = method.GetParameters();
                //var expectedGenericTypes = method.IsGenericMethodDefinition ? method.GetGenericArguments() : new Type[0];
                //Interface.GetMod().RootLogger.Write(LogType.Info, "IsGeneric: " + method.IsGenericMethodDefinition + " Params: " + string.Join(",", expectedGenericTypes.Select((o) => o.ToString()).ToArray()));
                var template = JavaScriptEngine.CreateFunctionTemplate(name);
                //var name1 = name;
                //Dictionary<int, object[]> convertedArgumentArrayCache = new Dictionary<int, object[]>();
                //convertedArgumentArrayCache[expectedParameters.Length] = new object[expectedParameters.Length];
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
                    //Manager.Logger.Write(LogType.Info, "Callback: " + name1 + " Params: " + string.Join(",", convertedArguments.Select((o)=> o.ToString()).ToArray()));
                    var result = method.Invoke(library, convertedArguments);
                    return method.ReturnType == typeof(void) ? InternalHandle.Empty : JavaScriptEngine.CreateValue(result, true);
                });
                scope.SetProperty(name, func);
                funcs.Add(func);
            }
        }

        /// <summary>
        /// Loads plugin watchers used by this extension
        /// </summary>
        /// <param name="plugindir"></param>
        public override void LoadPluginWatchers(string plugindir)
        {
            // Register the watcher
            watcher = new FSWatcher(plugindir, "*.js");
            Manager.RegisterPluginChangeWatcher(watcher);
            loader.Watcher = watcher;
        }

        /// <summary>
        /// Called when all other extensions have been loaded
        /// </summary>
        public override void OnModLoad()
        {
            // Bind JavaScript specific libraries
            LoadLibrary(new JavaScriptGlobal(Manager.Logger), "");
            LoadLibrary(new JavaScriptDatafile(JavaScriptEngine), "data");

            // Bind any libraries to JavaScript
            foreach (string name in Manager.GetLibraries())
            {
                LoadLibrary(Manager.GetLibrary(name), name.ToLowerInvariant());
            }

            // Extension to webrequests
            LoadLibrary(new JavaScriptWebRequests(), "webrequests");
        }
    }
}
