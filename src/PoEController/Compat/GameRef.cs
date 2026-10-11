using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PoEController.Compat
{
    /// <summary>
    /// Reads the game's UI fields (buttons, labels, panels) by name instead of compiling the references in, so a
    /// renamed or removed member after a game change means "not found" (logged once) rather than an exception that
    /// would take the calling feature down. Fields and properties, public or not; lookups are cached per type.
    /// </summary>
    internal static class GameRef
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<string, MemberInfo> s_Cache = new Dictionary<string, MemberInfo>();
        private static readonly HashSet<string> s_Missing = new HashSet<string>();

        /// <summary>The member's value as <typeparamref name="T"/>, or null if missing / null / another type.</summary>
        public static T Get<T>(object obj, string name) where T : class
        {
            return obj != null && TryRead(obj, name, out object v) ? v as T : null;
        }

        /// <summary>A value-type member (bool, enum…); false if missing.</summary>
        public static bool TryGet<T>(object obj, string name, out T value) where T : struct
        {
            if (obj != null && TryRead(obj, name, out object v) && v is T t)
            {
                value = t;
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>The GameObject behind a member holding a GameObject or any Component (e.g. a button widget).</summary>
        public static GameObject Object(object obj, string name)
        {
            if (obj == null || !TryRead(obj, name, out object v))
            {
                return null;
            }
            if (v is GameObject go)
            {
                return go != null ? go : null;
            }
            return v is Component c && c != null ? c.gameObject : null;
        }

        public static bool Set(object obj, string name, object value)
        {
            MemberInfo m = obj != null ? Find(obj.GetType(), name) : null;
            try
            {
                if (m is FieldInfo f)
                {
                    f.SetValue(obj, value);
                    return true;
                }
                if (m is PropertyInfo p && p.CanWrite)
                {
                    p.SetValue(obj, value, null);
                    return true;
                }
            }
            catch (Exception)
            {
                // Wrong type: treat as missing.
            }
            return false;
        }

        /// <summary>Does <paramref name="type"/> (or a base type) have a field or property called <paramref name="name"/>?</summary>
        public static bool Has(Type type, string name) => Find(type, name, logMissing: false) != null;

        private static bool TryRead(object obj, string name, out object value)
        {
            value = null;
            MemberInfo m = Find(obj.GetType(), name);
            try
            {
                if (m is FieldInfo f)
                {
                    value = f.GetValue(obj);
                    return true;
                }
                if (m is PropertyInfo p && p.CanRead && p.GetIndexParameters().Length == 0)
                {
                    value = p.GetValue(obj, null);
                    return true;
                }
            }
            catch (Exception)
            {
                // A getter that throws counts as missing.
            }
            return false;
        }

        private static MemberInfo Find(Type type, string name, bool logMissing = true)
        {
            string key = type.FullName + "." + name;
            if (s_Cache.TryGetValue(key, out MemberInfo cached))
            {
                return cached;
            }
            MemberInfo found = null;
            for (Type t = type; t != null && found == null; t = t.BaseType)
            {
                found = (MemberInfo)t.GetField(name, All | BindingFlags.DeclaredOnly) ?? t.GetProperty(name, All | BindingFlags.DeclaredOnly);
            }
            s_Cache[key] = found;
            if (found == null && logMissing && s_Missing.Add(key))
            {
                Plugin.Log.LogWarning($"Game member not found: {key} (the part using it does nothing)");
            }
            return found;
        }
    }
}
