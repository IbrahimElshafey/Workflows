using System;
using System.Collections.Generic;
using Workflows.Shared.Serialization;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class JsonObjectSerializerTests
    {
        public class TestTarget
        {
            public int Number { get; set; }
            public string? Text { get; set; }
            public List<string>? Items { get; set; }
            public int[]? Array { get; set; }
            public bool Flag { get; set; }
        }

        [Fact]
        public void Serializer_SkipsDefaultValuesAndEmptyCollections()
        {
            var serializer = new JsonObjectSerializer();
            var target = new TestTarget
            {
                Number = 0, // default int
                Text = null, // default string
                Items = new List<string>(), // empty collection
                Array = System.Array.Empty<int>(), // empty array
                Flag = false // default bool
            };

            var serialized = serializer.Serialize(target);
            var json = serialized.ToString();

            // None of these properties should be in the serialized JSON
            Assert.DoesNotContain("\"Number\"", json);
            Assert.DoesNotContain("\"Text\"", json);
            Assert.DoesNotContain("\"Items\"", json);
            Assert.DoesNotContain("\"Array\"", json);
            Assert.DoesNotContain("\"Flag\"", json);
        }

        [Fact]
        public void Serializer_IncludesNonDefaultValuesAndNonEmptyCollections()
        {
            var serializer = new JsonObjectSerializer();
            var target = new TestTarget
            {
                Number = 42,
                Text = "hello",
                Items = new List<string> { "item" },
                Array = new int[] { 1, 2, 3 },
                Flag = true
            };

            var serialized = serializer.Serialize(target);
            var json = serialized.ToString();

            // All of these properties should be in the serialized JSON
            Assert.Contains("\"Number\"", json);
            Assert.Contains("\"Text\"", json);
            Assert.Contains("\"Items\"", json);
            Assert.Contains("\"Array\"", json);
            Assert.Contains("\"Flag\"", json);
        }
    }
}
