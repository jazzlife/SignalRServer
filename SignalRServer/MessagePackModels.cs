using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;

namespace SignalRServer
{
    [MessagePackObject]
    public class SignalRMessage
    {
        [Key(0)]
        public string From { get; set; } = "Server";

        [Key(1)]
        public string To { get; set; } = "All";
        [Key(2)]
        public string Command { get; set; } = "Update";     // PowerOn, PowerOff, Update, etc.
        [Key(3)]
        public string DataType { get; set; } = "String";    // Type Name (e.g., System.String, System.Int32, MyNamespace.MyClass, etc.)
        [Key(4)]
        public object Data { get; set; } = "";              // Typed Data
    }

    [MessagePackObject]
    public class StateMessage
    {
        [Key(0)]
        public string Who { get; set; } = "Unknown";            // e.g., User Name or Device ID
        [Key(1)]
        public string State { get; set; } = "Disconnected";     // e.g., Online, Offline, Busy, etc.
        [Key(2)]
        public string Description { get; set; } = "";           // Optional description
    }

    [MessagePackObject]
    public class ProgressData
    {
        [Key(0)]
        public string FromGUID { get; set; }
        [Key(1)]
        public int Porgress { get; set; }
    }

    public static class ObjectArrayMapper
    {
        private static readonly ConcurrentDictionary<Type, Func<object[], object>> _cache = new();

        // 외부 API
        public static T MapFromArray<T>(object[] values) where T : new()
            => (T)MapFromArray(typeof(T), values);

        // 내부 비제네릭
        public static object MapFromArray(Type type, object[] values)
        {
            var mapper = _cache.GetOrAdd(type, BuildMapper);
            return mapper(values);
        }

        private static Func<object[], object> BuildMapper(Type type)
        {
            if (type.GetCustomAttribute<MessagePackObjectAttribute>() == null)
                throw new InvalidOperationException($"{type.Name} must be marked with [MessagePackObject].");

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .OrderBy(p => p.GetCustomAttributes(typeof(KeyAttribute), false)
                                .Cast<KeyAttribute>()
                                .FirstOrDefault()?.IntKey ?? p.MetadataToken)
                .ToArray();

            var valuesParam = Expression.Parameter(typeof(object[]), "values");
            var objVar = Expression.Variable(type, "obj");
            var blockExprs = new List<Expression>
                {
                    Expression.Assign(objVar, Expression.New(type))
                };

            for (int i = 0; i < props.Length; i++)
            {
                var prop = props[i];
                var targetType = prop.PropertyType;
                var valueObj = Expression.ArrayIndex(valuesParam, Expression.Constant(i));

                var assignExpr = BuildAssignment(objVar, prop, valueObj, targetType);

                var ifNotNull = Expression.IfThen(
                    Expression.NotEqual(valueObj, Expression.Constant(null)),
                    assignExpr
                );

                blockExprs.Add(ifNotNull);
            }

            blockExprs.Add(objVar);

            var body = Expression.Block(new[] { objVar }, blockExprs);
            return Expression.Lambda<Func<object[], object>>(body, valuesParam).Compile();
        }

        private static Expression BuildAssignment(ParameterExpression objVar, PropertyInfo prop, Expression valueObj, Type targetType)
        {
            // 단순 타입
            if (IsSimpleType(targetType))
            {
                var convertMethod = typeof(ObjectArrayMapper).GetMethod(nameof(FastConvert),
                    BindingFlags.NonPublic | BindingFlags.Static);
                var call = Expression.Call(convertMethod, valueObj, Expression.Constant(targetType));
                var valueCast = Expression.Convert(call, targetType);
                return Expression.Assign(Expression.Property(objVar, prop), valueCast);
            }

            // 배열 / 리스트
            if (targetType.IsArray ||
                (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>)))
            {
                var elementType = targetType.IsArray ? targetType.GetElementType() : targetType.GetGenericArguments()[0];
                var convertMethod = typeof(ObjectArrayMapper).GetMethod(nameof(ConvertCollection),
                    BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(elementType);
                var call = Expression.Call(convertMethod,
                    Expression.Convert(valueObj, typeof(object[])),
                    Expression.Constant(targetType));
                return Expression.Assign(Expression.Property(objVar, prop), call);
            }

            // Dictionary<TKey, TValue>
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var args = targetType.GetGenericArguments();
                var keyType = args[0];
                var valueType = args[1];
                var convertMethod = typeof(ObjectArrayMapper).GetMethod(nameof(ConvertDictionary),
                    BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(keyType, valueType);
                var call = Expression.Call(convertMethod, Expression.Convert(valueObj, typeof(object[])));
                return Expression.Assign(Expression.Property(objVar, prop), call);
            }

            // 중첩 DTO
            if ((targetType.IsClass || targetType.IsValueType) &&
                targetType.GetCustomAttribute<MessagePackObjectAttribute>() != null)
            {
                var mapMethod = typeof(ObjectArrayMapper).GetMethod(nameof(MapFromArray),
                    new[] { typeof(Type), typeof(object[]) });
                var nestedCall = Expression.Call(mapMethod,
                    Expression.Constant(targetType),
                    Expression.Convert(valueObj, typeof(object[])));
                return Expression.Assign(Expression.Property(objVar, prop), Expression.Convert(nestedCall, targetType));
            }

            // fallback
            var changeTypeCall = Expression.Call(typeof(Convert), nameof(Convert.ChangeType), null,
                valueObj, Expression.Constant(targetType));
            var converted = Expression.Convert(changeTypeCall, targetType);
            return Expression.Assign(Expression.Property(objVar, prop), converted);
        }

        // 컬렉션 변환
        private static object ConvertCollection<T>(object[] values, Type targetType)
        {
            var list = new List<T>();
            foreach (var v in values)
            {
                if (v == null)
                {
                    list.Add(default);
                    continue;
                }

                if (v is object[] nested && !IsSimpleType(typeof(T)))
                {
                    var obj = MapFromArray(typeof(T), nested);
                    list.Add((T)obj);
                }
                else
                {
                    list.Add((T)FastConvert(v, typeof(T)));
                }
            }
            return targetType.IsArray ? list.ToArray() : list;
        }

        // Dictionary 변환
        private static Dictionary<TKey, TValue> ConvertDictionary<TKey, TValue>(object[] values)
        {
            var dict = new Dictionary<TKey, TValue>();
            foreach (var entry in values)
            {
                if (entry is object[] kv && kv.Length == 2)
                {
                    var key = (TKey)FastConvert(kv[0], typeof(TKey));
                    TValue val;
                    if (kv[1] is object[] nested && !IsSimpleType(typeof(TValue)))
                    {
                        var obj = MapFromArray(typeof(TValue), nested);
                        val = (TValue)obj;
                    }
                    else
                    {
                        val = (TValue)FastConvert(kv[1], typeof(TValue));
                    }
                    dict[key] = val;
                }
            }
            return dict;
        }

        // 빠른 단순 변환
        private static object FastConvert(object value, Type targetType)
        {
            if (value == null) return null;

            // 원래 타입이면 바로 반환
            if (targetType.IsInstanceOfType(value)) return value;

            if (targetType == typeof(string)) return value.ToString();
            if (targetType == typeof(int)) return Convert.ToInt32(value);
            if (targetType == typeof(long)) return Convert.ToInt64(value);
            if (targetType == typeof(float)) return Convert.ToSingle(value);
            if (targetType == typeof(double)) return Convert.ToDouble(value);
            if (targetType == typeof(decimal)) return Convert.ToDecimal(value);
            if (targetType == typeof(bool)) return Convert.ToBoolean(value);
            if (targetType == typeof(byte[])) return value as byte[];

            if (targetType == typeof(Guid))
            {
                if (value is Guid g) return g;
                if (value is string s) return Guid.Parse(s);
                if (value is byte[] b && b.Length == 16) return new Guid(b);
            }

            if (targetType == typeof(DateTime))
            {
                if (value is DateTime dt) return dt;
                if (value is string s) return DateTime.Parse(s);
                if (value is long ticks) return new DateTime(ticks);
            }

            if (targetType.IsEnum)
            {
                if (value is string s) return Enum.Parse(targetType, s);
                return Enum.ToObject(targetType, value);
            }

            return Convert.ChangeType(value, targetType);
        }

        private static bool IsSimpleType(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(byte[]);
    }

    // 확장 메서드
    public static class ObjectArrayExtensions
    {
        public static T ToDto<T>(this object[] values) where T : new()
            => ObjectArrayMapper.MapFromArray<T>(values);

        public static T ToDto<T>(this object value) where T : new()
        {
            try
            {
                if (value is object[] arr)
                    return ObjectArrayMapper.MapFromArray<T>(arr);
            }
            catch (Exception ex) { }
            return new T();
        }
    }

    // SignalR 확장
    public static class SignalRExtensions
    {
        public static void OnSignalRMessage(this HubConnection connection, string methodName, Action<SignalRMessage> handler)
        {
            try
            {
                connection.On<byte[]>(methodName, data =>
                {
                    var _msg = MessagePackSerializer.Deserialize<SignalRMessage>(data);

                    handler(_msg);
                });
            }
            catch (Exception ex) { handler(new SignalRMessage()); }
        }
    }
}
