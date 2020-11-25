using System;
using System.Collections.Generic;
using System.Text;

namespace Zadatak1
{
    public class SharedResources
    {
        //cuvas ih kao ime, (Tip, vrijednost)
        private static Dictionary<string, (Type, object)> _globals = new Dictionary<string, (Type, object)>();

        public static T ReadValue<T>(string name)
        {
            lock (_globals)
            {
                if (_globals.ContainsKey(name))
                    return (T)Convert.ChangeType(_globals[name].Item2, _globals[name].Item1);
                else
                    return default(T);
            }
        }

        public static void WriteValue(string name, Type type, object value)
        {
            lock (_globals)
            {
                _globals[name] = (type, value);
            }
        }

        public static void WriteValue(string name, object input)
        {
            var parsed = ParseInputVariable(input);
            WriteValue(name, parsed.Item1, parsed.Item2);
        }

        public static (Type, object) ParseInputVariable(object input)
        {
            return (input.GetType(), input);
        }
    }
}
