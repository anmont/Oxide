using System.Collections.Generic;

using V8.Net;

using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries;

namespace Oxide.Ext.JavaScript.Libraries
{
    /// <summary>
    /// A datafile library that allows JavaScript to access datafiles
    /// </summary>
    public class JavaScriptDatafile : Library
    {
        /// <summary>
        /// Returns if this library should be loaded into the global namespace
        /// </summary>
        public override bool IsGlobal { get { return false; } }

        /// <summary>
        /// Gets the JavaScript engine
        /// </summary>
        public V8Engine JavaScriptEngine { get; private set; }

        // The data file map
        private readonly Dictionary<DynamicConfigFile, InternalHandle> datafilemap;

        /// <summary>
        /// Initializes a new instance of the JavaScriptDatafile class
        /// <param name="engine"></param>
        /// </summary>
        public JavaScriptDatafile(V8Engine engine)
        {
            datafilemap = new Dictionary<DynamicConfigFile, InternalHandle>();
            JavaScriptEngine = engine;
        }

        /// <summary>
        /// Gets a datatable
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        [LibraryFunction("GetData")]
        public InternalHandle GetData(string name)
        {
            // Get the data file
            DynamicConfigFile datafile = Interface.GetMod().DataFileSystem.GetDatafile(name);
            if (datafile == null) return null;

            // Check if it already exists
            InternalHandle obj;
            if (datafilemap.TryGetValue(datafile, out obj)) return obj;

            // Create the table
            obj = Utility.ObjectFromConfig(datafile, JavaScriptEngine);
            datafilemap.Add(datafile, obj);

            // Return
            return obj;
        }

        /// <summary>
        /// Saves a datatable
        /// </summary>
        /// <param name="name"></param>
        [LibraryFunction("SaveData")]
        public void SaveData(string name)
        {
            // Get the data file
            DynamicConfigFile datafile = Interface.GetMod().DataFileSystem.GetDatafile(name);
            if (datafile == null) return;

            // Get the table
            InternalHandle obj;
            if (!datafilemap.TryGetValue(datafile, out obj)) return;

            // Copy and save
            Utility.SetConfigFromObject(datafile, obj);
            Interface.GetMod().DataFileSystem.SaveDatafile(name);
        }
    }
}
