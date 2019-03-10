﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace JDanielSmith.Runtime.InteropServices
{
	public class VCEntrypointMangler : IEntrypointMangler
	{
		internal class TypeToString
		{
			readonly Dictionary<Type, string> typeToString_ = new Dictionary<Type, string>
			{
				// https://en.wikiversity.org/wiki/Visual_C%2B%2B_name_mangling
				{ typeof(SByte), "C" }, // int8_t
				// "D" // char
				{ typeof(Byte), "E" }, // uint8_t, unsigned char

				{ typeof(Int16), "F" },
				{ typeof(UInt16), "G" },

				{ typeof(Int32), "H" },
				{ typeof(UInt32), "I" },

				{ typeof(Int64), "_J" },
				{ typeof(UInt64), "_K" },

				{ typeof(float), "M" },
				{ typeof(double), "N" },

				{ typeof(Char), "_W" }, // wchar_t

				{ typeof(void), "X" },
			};

			public TypeToString()
			{
				// Type modifier
				// A - reference
				// P - pointer

				// CV prefix
				// E __ptr64

				// CV modifier
				// A - none
				// B - const
				typeToString_[Type.GetType("System.Int32&")] = "PEA" + typeToString_[typeof(Int32)];
			}

			private string typeToString(Type type, CharSet charSet)
			{
				if ((charSet == CharSet.Ansi) && (type == typeof(Char)))
				{
					return "D"; // char
				}

				return typeToString_[type];
			}

			public string AsString(Type type, CharSet charSet = CharSet.Unicode)
			{
				string retval = "";
				if (type == typeof(String))
				{
					retval = "PEB"; // const __int64 pointer
					type = typeof(Char);
				}

				return retval + typeToString(type, charSet);
			}
		}

		static readonly TypeToString typeToString = new TypeToString();

		string getReturn(ParameterInfo returnParameter, CharSet charSet)
		{
			// https://en.wikiversity.org/wiki/Visual_C%2B%2B_name_mangling
			// A - no CV modifier
			string ret = "A";
			string value = typeToString.AsString(returnParameter.ParameterType, charSet);
			return ret + value;
		}

		string getParameter(ParameterInfo parameter, CharSet charSet)
		{
			// Type modifier
			// A - reference
			// P - pointer

			// CV prefix
			// E __ptr64

			// CV modifier
			// A - none
			// B - const

			// "const wchar_t*" -> PEB_W

			return typeToString.AsString(parameter.ParameterType, charSet);
		}

		private static string getCppNamespace(MethodInfo method)
		{
			var ns = method.DeclaringType.Namespace;

			// We'll look for the start of a C++ namespace in one of three places: "global::DllImport" (sure, why not?),
			// "... .DllImport. ...", and "... ._. ..." (because "DllImport" is a lot of typing).
			int skip = 1;
			int cppNsStart = ns.IndexOf("DllImport.", StringComparison.InvariantCulture);
			if (cppNsStart < 0)
			{
				skip = 2;
				cppNsStart = ns.IndexOf(".DllImport.", StringComparison.InvariantCulture);
				if (cppNsStart < 0)
				{
					cppNsStart = ns.IndexOf("._.", StringComparison.InvariantCulture);
				}
			}

			string retval = "";
			if (cppNsStart >= 0)
			{
				var cppNs = ns.Substring(cppNsStart);
				var names = cppNs.Split('.');
				var cppNames = names.Skip(skip).Reverse(); // ignore the marker and reverse the order
				foreach (var cppName in cppNames)
				{
					retval += "@" + cppName;
				}
			}

			return retval;
		}

		private static string getName(MethodInfo method, System.Runtime.InteropServices.ComTypes.FUNCKIND funcKind)
		{
			var cppNs = getCppNamespace(method);

            var methodName = method.Name;

            if (funcKind == System.Runtime.InteropServices.ComTypes.FUNCKIND.FUNC_NONVIRTUAL) // i.e., "static" method
            {
                cppNs = "@" + method.DeclaringType.Name + cppNs;
            }
            else if (funcKind == System.Runtime.InteropServices.ComTypes.FUNCKIND.FUNC_VIRTUAL) // i.e., instance method
            {
                cppNs = "@" + method.DeclaringType.Name + cppNs;

                int lastIndex_const = methodName.LastIndexOf("_const", StringComparison.Ordinal);
                bool isConstMethod = lastIndex_const == (methodName.Length - "_const".Length);
                methodName = isConstMethod ? methodName.Remove(lastIndex_const, "_const".Length) : methodName;
            }

            // foo - name
            return methodName + cppNs;
		}

		public string Mangle(MethodInfo method, System.Runtime.InteropServices.ComTypes.FUNCKIND funcKind, CharSet charSet = CharSet.Unicode)
		{
			string access = "Y"; // "none" (not public/private/protected static/virtual/thunk)
			if (funcKind == System.Runtime.InteropServices.ComTypes.FUNCKIND.FUNC_NONVIRTUAL)
			{
				access = "S"; // "static"
			}
            else if (funcKind == System.Runtime.InteropServices.ComTypes.FUNCKIND.FUNC_VIRTUAL) // i.e., instance method
            {
                access = "QE"; // member function, __thiscall,

                bool constMethod = method.Name.EndsWith("_const", StringComparison.Ordinal);
                access += constMethod ? "B" : "A";
            }

            var methodParameters_ = method.GetParameters();
			// first parameter is "this", don't use it for mangling
			int skip = funcKind == System.Runtime.InteropServices.ComTypes.FUNCKIND.FUNC_VIRTUAL ? 1 : 0; // i.e., instance method
            var methodParameters = methodParameters_.Skip(skip);

            string parameters = String.Empty;
			foreach (var parameter in methodParameters)
			{
				parameters += getParameter(parameter, charSet);
			}
			if (!String.IsNullOrWhiteSpace(parameters))
				parameters += "@"; // end of parameter list
			else
				parameters = typeToString.AsString(typeof(void));

			var returnType = getReturn(method.ReturnParameter, charSet);

			// ? - decorated name
			// name@ - name fragment
			// @Z - end
			return "?" + getName(method, funcKind) + "@@" + access + returnType + parameters + "Z";
		}
	}
}
