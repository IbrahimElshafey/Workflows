using System;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Definition
{
    /// <summary>
    /// Bridge that converts runtime <see cref="Wait"/> objects to serializable DTOs.
    /// The runner sets the converter before invoking workflow methods so that
    /// workflow methods can yield <see cref="Wait"/> objects while returning
    /// <see cref="IAsyncEnumerable{WaitInfrastructureDto}"/>.
    /// </summary>
    public static class WaitDtoConversion
    {
        private static readonly System.Threading.AsyncLocal<Func<Wait, WaitInfrastructureDto>> _converter = new();

        /// <summary>
        /// Gets or sets the converter used by the implicit <see cref="Wait"/> to DTO conversion.
        /// </summary>
        public static Func<Wait, WaitInfrastructureDto> Converter
        {
            get => _converter.Value;
            set => _converter.Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Converts a <see cref="Wait"/> to a <see cref="WaitInfrastructureDto"/> using the configured converter.
        /// </summary>
        public static WaitInfrastructureDto Convert(Wait wait)
        {
            if (wait == null) return null;
            var currentConverter = _converter.Value;
            if (currentConverter == null)
            {
                throw new InvalidOperationException(
                    "WaitDtoConversion.Converter has not been set. The runner must configure this converter before invoking workflow methods.");
            }
            return currentConverter(wait);
        }
    }
}
