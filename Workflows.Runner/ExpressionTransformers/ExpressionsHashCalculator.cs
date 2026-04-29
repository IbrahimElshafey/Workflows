using System;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;

namespace Workflows.Runner.ExpressionTransformers;

public class ExpressionsHashCalculator
{
    public byte[] HashValue { get; }
    public ExpressionsHashCalculator(
        LambdaExpression matchExpression,
        string afterMatchAction,
        string cancelAction)
    {
        try
        {
            HashValue = GetHash(matchExpression, afterMatchAction, cancelAction);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private byte[] GetHash(LambdaExpression matchExpression, string afterMatchAction, string cancelAction)
    {
        var sb = new StringBuilder();
        if (matchExpression != null)
            sb.Append(matchExpression.ToString());

        if (afterMatchAction != null)
            sb.Append(afterMatchAction);

        if (cancelAction != null)
            sb.Append(cancelAction);
        var MD5 = new MD5CryptoServiceProvider();
        return MD5.ComputeHash(Encoding.Unicode.GetBytes(sb.ToString()));
    }

}
