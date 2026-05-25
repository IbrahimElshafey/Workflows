using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class DateTimeUtcConverter : ValueConverter<DateTime, DateTime>
    {
        public DateTimeUtcConverter() : base(
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }
}
