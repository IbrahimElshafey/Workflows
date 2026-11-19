using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class NullableDateTimeUtcConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableDateTimeUtcConverter() : base(
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null)
        {
        }
    }
}
