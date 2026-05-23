using System;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Schema.Generation;

namespace Workflows.TestShell
{
    public class MockSchemaGenerator : JSchemaGenerator
    {
        public override JSchema Generate(Type type)
        {
            return JSchema.Parse("{}");
        }

        public override JSchema Generate(Type type, bool rootSchemaNullable)
        {
            return JSchema.Parse("{}");
        }
    }
}
