using System;
using System.Collections.Generic;
using System.Reflection;

using V8.Net;

using Oxide.Core.Configuration;

namespace Oxide.Ext.JavaScript
{
    /// <summary>
    /// Contains extension and utility methods
    /// </summary>
    public static class Utility
    {
        /// <summary>
        /// Copies and translates the contents of the specified table into the specified config file
        /// </summary>
        /// <param name="config"></param>
        /// <param name="objectInstance"></param>
        public static void SetConfigFromObject(DynamicConfigFile config, InternalHandle objectInstance)
        {
            config.Clear();
            foreach (var property in objectInstance.GetPropertyNames())
            {
                if (objectInstance.GetProperty(property).ValueType == JSValueType.Undefined) continue;
                object value = objectInstance.GetProperty(property).As<object>();
                if (value != null) config[property] = value;
            }
        }

        /// <summary>
        /// Copies and translates the contents of the specified config file into the specified object
        /// </summary>
        /// <param name="config"></param>
        /// <param name="engine"></param>
        /// <returns></returns>
        public static InternalHandle ObjectFromConfig(DynamicConfigFile config, V8Engine engine)
        {
            var tbl = engine.CreateObject();
            // Loop each item in config
            foreach (var pair in config)
            {
                // Translate and set on object
                tbl.SetProperty(pair.Key, engine.CreateValue(pair.Value));
            }

            // Return
            return tbl;
        }

        /// <summary>
        /// Gets the namespace of the specified type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static string GetNamespace(Type type)
        {
            return type.Namespace ?? string.Empty;
        }

        public static IEnumerable<Type> GetAllTypesFromAssembly(Assembly asm)
        {
            foreach (var module in asm.GetModules())
            {
                Type[] moduleTypes;
                try
                {
                    moduleTypes = module.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    moduleTypes = e.Types;
                }
                catch (Exception)
                {
                    moduleTypes = new Type[0];
                }

                foreach (var type in moduleTypes)
                {
                    if (type != null)
                    {
                        yield return type;
                    }
                }
            }
        }
    }
}
