using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Workflows.Abstraction.DTOs;
using Workflows.Definition;

namespace Workflows.Runner.Helpers
{
    internal static class CompilerConstants
    {
        public const string StateFieldName = "<>1__state";
        public const string CallerSuffix = "<>4__this";
        public const string LiftedLocalMarker = "5__";
        public const string SynthesizedLocalMarker = "<>s__"; // <-- New addition
        public const string ClosurePrefix = "<>c__DisplayClass";
        public const string ClosureFieldPrefix = "<>8__";

        // Legacy support
        public const string LegacyLocalWrapMarker = "wrap";
    }
}