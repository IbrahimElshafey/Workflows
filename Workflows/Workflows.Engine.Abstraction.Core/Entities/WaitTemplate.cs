using Workflows.Handler.InOuts.Entities.EntityBehaviour;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System;
using System.Collections.Generic;
using Workflows.Handler.Expressions;
using Workflows.Handler.Helpers;
namespace Workflows.Handler.InOuts.Entities
{
    public class WaitTemplate : IEntity<int>, IBeforeSaveEntity
    {
        public int Id { get; internal set; }
        public int WorkflowId { get; internal set; }
        public int? MethodId { get; internal set; }
        public int MethodGroupId { get; internal set; }
        public MethodsGroup MethodGroup { get; internal set; }
        public byte[] Hash { get; internal set; }
        public DateTime Created { get; internal set; }
        public DateTime DeactivationDate { get; internal set; }
        public bool IsMandatoryPartFullMatch { get; internal set; }

        internal string MatchExpressionValue { get; set; }

        public string CancelMethodAction { get; internal set; }

        public LambdaExpression MatchExpression { get; internal set; }

        public List<string> CallMandatoryPartPaths { get; internal set; }

        public LambdaExpression InstanceMandatoryPartExpression { get; internal set; }
        internal string InstanceMandatoryPartExpressionValue { get; set; }



        public string AfterMatchAction { get; internal set; }


        public int? ServiceId { get; internal set; }
        public int InCodeLine { get; internal set; }
        public int IsActive { get; internal set; } = 1;
        //public List<MethodWaitEntity> Waits { get; internal set; }

        bool expressionsLoaded;
        internal void LoadUnmappedProps(bool forceReload = false)
        {
            try
            {
                if (expressionsLoaded && !forceReload) return;

                if (MatchExpressionValue != null)
                    MatchExpression = Dependencies.ExpressionSerializer.Deserialize(MatchExpressionValue);
                if (InstanceMandatoryPartExpressionValue != null)
                    InstanceMandatoryPartExpression = Dependencies.ExpressionSerializer.Deserialize(InstanceMandatoryPartExpressionValue);

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            expressionsLoaded = true;
        }

        internal static byte[] CalcHash(LambdaExpression matchExpression, LambdaExpression setDataExpression)
        {
            var matchBytes = Encoding.UTF8.GetBytes(Dependencies.ExpressionSerializer.Serialize(matchExpression));
            var setDataBytes = Encoding.UTF8.GetBytes(Dependencies.ExpressionSerializer.Serialize(setDataExpression));
            using var md5 = MD5.Create();
            var mergedHash = new byte[matchBytes.Length + setDataBytes.Length];
            Array.Copy(matchBytes, mergedHash, matchBytes.Length);
            Array.Copy(setDataBytes, 0, mergedHash, matchBytes.Length, setDataBytes.Length);

            return md5.ComputeHash(mergedHash);
        }

        public void BeforeSave()
        {
            if (MatchExpression != null)
                MatchExpressionValue = Dependencies.ExpressionSerializer.Serialize(MatchExpression);
            if (InstanceMandatoryPartExpression != null)
                InstanceMandatoryPartExpressionValue = Dependencies.ExpressionSerializer.Serialize(InstanceMandatoryPartExpression);
        }
    }
}
