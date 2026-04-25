using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Workflows.Handler.Abstraction.Serialization;
using Workflows.Handler.Expressions;
using Workflows.Handler.InOuts;

namespace Workflows.Handler.Helpers
{
    public static class CoreExtensions
    {
        public static bool CanConvertToSimpleString(this Type type)
        {
            var types = new[] { typeof(bool), typeof(byte), typeof(sbyte), typeof(char), typeof(decimal), typeof(double), typeof(float), typeof(int), typeof(uint),
                typeof(long), typeof(ulong), typeof(short), typeof(ushort), typeof(string) };
            return types.Contains(type) || type == typeof(DateTime) || type == typeof(Guid) || type.IsEnum;
        }

        public static bool CanConvertToSimpleString(this object ob) =>
            ob != null && ob.GetType().CanConvertToSimpleString();

        public static MethodInfo GetMethodInfo<T>(Expression<Func<T, object>> methodSelector) =>
            GetMethodInfoWithType(methodSelector).MethodInfo;

        public static (MethodInfo MethodInfo, Type OwnerType) GetMethodInfoWithType<T>(Expression<Func<T, object>> methodSelector)
        {
            MethodInfo mi = null;
            Type ownerType = null;
            var visitor = new GenericVisitor();
            visitor.OnVisitMethodCall(VisitMethod);
            visitor.OnVisitConstant(VisitConstant);
            visitor.Visit(methodSelector);
            return (mi, ownerType);

            Expression VisitMethod(MethodCallExpression node)
            {
                mi = node.Method;
                return node;
            }

            Expression VisitConstant(ConstantExpression node)
            {
                ownerType = node.Type;
                return node;
            }
        }

        public static MethodInfo GetMethodInfo(string AssemblyName, string ClassName, string MethodName, string MethodSignature)
        {
            MethodInfo methodInfo = null;
            var assemblyPath = $"{AppContext.BaseDirectory}{AssemblyName}.dll";
            if (System.IO.File.Exists(assemblyPath))
                if (AssemblyName != null && ClassName != null && MethodName != null)
                {
                    methodInfo = Assembly.LoadFrom(assemblyPath)
                        .GetType(ClassName)
                        ?.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(x => x.Name == MethodName && InOuts.MethodData.CalcSignature(x) == MethodSignature);
                }
            return methodInfo;
        }

        public static BindingFlags DeclaredWithinTypeFlags() =>
            BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        public static string GetFullName(this MethodBase method)
        {
            return $"{method.DeclaringType.FullName}.{method.Name}";
        }
        public static bool IsAsyncMethod(this MethodBase method)
        {
            var attribute =
                method.GetCustomAttribute(typeof(AsyncStateMachineAttribute)) ??
                method.GetCustomAttribute(typeof(AsyncIteratorStateMachineAttribute));

            if (attribute != null) return true;

            return
                method is MethodInfo { ReturnType.IsGenericType: true } mi &&
                mi.ReturnType.GetGenericTypeDefinition() == typeof(Task<>);
        }
        public static IEnumerable<T> Flatten<T>(
           this IEnumerable<T> e,
           Func<T, IEnumerable<T>> f) =>
               e.SelectMany(c => f(c).Flatten(f)).Concat(e);

        public static IEnumerable<T> Flatten<T>(this T value, Func<T, IEnumerable<T>> childrens)
        {
            foreach (var currentItem in childrens(value))
            {
                foreach (var currentChild in Flatten(currentItem, childrens))
                {
                    yield return currentChild;
                }
            }
            yield return value;
        }
        public static (bool IsWorkflowData, MemberExpression NewExpression) GetDataParameterAccess(
        this MemberExpression node,
        ParameterExpression workflowInstanceArg)
        {
            var propAccessStack = new Stack<MemberInfo>();
            var isWorkflowData = IsDataAccess(node);
            if (isWorkflowData)
            {
                var newAccess = Expression.MakeMemberAccess(workflowInstanceArg, propAccessStack.Pop());
                while (propAccessStack.Count > 0)
                {
                    var currentProp = propAccessStack.Pop();
                    newAccess = Expression.MakeMemberAccess(newAccess, currentProp);
                }

                return (true, newAccess);
            }

            return (false, null);

            bool IsDataAccess(MemberExpression currentNode)
            {
                propAccessStack.Push(currentNode.Member);
                var subNode = currentNode.Expression;
                if (subNode == null) return false;
                //is workflow data access 
                var isWorkflowDataAccess =
                    subNode.NodeType == ExpressionType.Constant && subNode.Type == workflowInstanceArg.Type;
                if (isWorkflowDataAccess)
                    return true;
                if (subNode.NodeType == ExpressionType.MemberAccess)
                    return IsDataAccess((MemberExpression)subNode);
                return false;
            }
        }
        public static string GetRealTypeName(this Type t)
        {
            if (!t.IsGenericType)
                return t.Name;

            StringBuilder sb = new StringBuilder();
            sb.Append(t.Name.Substring(0, t.Name.IndexOf('`')));
            sb.Append('<');
            bool appendComma = false;
            foreach (Type arg in t.GetGenericArguments())
            {
                if (appendComma) sb.Append(',');
                sb.Append(GetRealTypeName(arg));
                appendComma = true;
            }
            sb.Append('>');
            return sb.ToString();
        }


        ////////////////
        public static bool SameMatchSignature(LambdaExpression replayMatch, LambdaExpression methodMatch)
        {
            var isEqual = replayMatch != null && methodMatch != null;
            if (isEqual is false) return false;
            if (replayMatch.ReturnType != methodMatch.ReturnType)
                return false;
            //if (replayMatch.Parameters.Count != methodMatch.Parameters.Count)
            //    return false;
            var minParamsCount = Math.Min(replayMatch.Parameters.Count, methodMatch.Parameters.Count);
            for (var i = 0; i < minParamsCount; i++)
                if (replayMatch.Parameters[i].Type != methodMatch.Parameters[i].Type)
                    return false;
            return true;
        }





        public static void CascadeSet<T, Prop>(
            this T objectToSet,
            Expression<Func<IEnumerable<T>>> childs,
            Expression<Func<Prop>> prop,
            Prop value)
        {
            //IsDeleted = true;
            //foreach (var child in ChildWaits)
            //{
            //    child.CascadeSetDeleted();
            //}
        }
        public static IEnumerable<Prop> CascadeGet<T, Prop>(
           this T objectToSet,
           Expression<Func<IEnumerable<T>>> childs,
           Expression<Func<Prop>> prop,
           Prop value)
        {
            //IsDeleted = true;
            //foreach (var child in ChildWaits)
            //{
            //    child.CascadeSetDeleted();
            //}
            return null;
        }


        //https://www.newtonsoft.com/json/help/html/serializationguide.htm
        //https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/built-in-types
        //https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/constants
        public static bool IsConstantType(this Type type)
        {
            var types = new[] { typeof(bool), typeof(byte), typeof(sbyte), typeof(char), typeof(decimal), typeof(double), typeof(float), typeof(int), typeof(uint), typeof(nint), typeof(nuint), typeof(int), typeof(uint), typeof(short), typeof(ushort), typeof(string) };
            return types.Contains(type);
        }

        //internal static Expression ToConstantExpression(this object result)
        //{
        //    if (result.GetType().IsConstantType())
        //    {
        //        return Constant(result);
        //    }
        //    //else if (expression.NodeType == ExpressionType.New)
        //    //    return expression;

        //    if (result is DateTime date)
        //    {
        //        return Constant(date.Ticks);
        //    }

        //    if (result is Guid guid)
        //    {
        //        return Constant(guid.ToString());
        //    }

        //    if (JsonConvert.SerializeObject(result) is string json)
        //    {
        //        return Constant(json);
        //    }
        //    throw new NotSupportedException(message:
        //           $"Can't evaluate object [{result}] to constant expression.");
        //}

        public static Type GetUnderlyingType(this MemberInfo member)
        {
            switch (member.MemberType)
            {
                case MemberTypes.Event:
                    return ((EventInfo)member).EventHandlerType;
                case MemberTypes.Field:
                    return ((FieldInfo)member).FieldType;
                case MemberTypes.Method:
                    return ((MethodInfo)member).ReturnType;
                case MemberTypes.Property:
                    return ((PropertyInfo)member).PropertyType;
                default:
                    throw new ArgumentException
                    (
                     "Input MemberInfo must be if type EventInfo, FieldInfo, MethodInfo, or PropertyInfo"
                    );
            }
        }

        //internal static void MergeIntoObject<T>(this JToken value, T target) where T : class
        //{
        //    using (var sr = value.CreateReader())
        //    {
        //        JsonSerializer.Create(PrivateDataResolver.Settings).Populate(sr, target);
        //    }
        //}
    }
}
