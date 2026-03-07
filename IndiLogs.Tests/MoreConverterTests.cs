using IndiLogs_3._0.Converters;
using System;
using System.Globalization;
using System.Windows.Data;
using Xunit;

namespace IndiLogs.Tests
{
    public class InverseBooleanConverterTests
    {
        [Fact]
        public void Convert_True_ReturnsFalse()
        {
            var converter = new InverseBooleanConverter();
            var result = converter.Convert(true, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void Convert_False_ReturnsTrue()
        {
            var converter = new InverseBooleanConverter();
            var result = converter.Convert(false, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void Convert_NonBool_ReturnsFalse()
        {
            var converter = new InverseBooleanConverter();
            var result = converter.Convert("notbool", typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void ConvertBack_True_ReturnsFalse()
        {
            var converter = new InverseBooleanConverter();
            var result = converter.ConvertBack(true, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void ConvertBack_False_ReturnsTrue()
        {
            var converter = new InverseBooleanConverter();
            var result = converter.ConvertBack(false, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }
    }

    public class EnumBoolConverterTests
    {
        private enum TestEnum { ValueA, ValueB, ValueC }

        [Fact]
        public void Convert_MatchingParameter_ReturnsTrue()
        {
            var converter = new EnumBoolConverter();
            var result = converter.Convert(TestEnum.ValueA, typeof(bool), TestEnum.ValueA, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void Convert_NonMatchingParameter_ReturnsFalse()
        {
            var converter = new EnumBoolConverter();
            var result = converter.Convert(TestEnum.ValueA, typeof(bool), TestEnum.ValueB, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void Convert_NullValue_ReturnsFalse()
        {
            var converter = new EnumBoolConverter();
            var result = converter.Convert(null, typeof(bool), TestEnum.ValueA, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void ConvertBack_True_ReturnsParameter()
        {
            var converter = new EnumBoolConverter();
            var result = converter.ConvertBack(true, typeof(TestEnum), TestEnum.ValueB, CultureInfo.InvariantCulture);
            Assert.Equal(TestEnum.ValueB, result);
        }

        [Fact]
        public void ConvertBack_False_ReturnsDoNothing()
        {
            var converter = new EnumBoolConverter();
            var result = converter.ConvertBack(false, typeof(TestEnum), TestEnum.ValueB, CultureInfo.InvariantCulture);
            Assert.Equal(Binding.DoNothing, result);
        }
    }

    public class NotNullConverterTests
    {
        [Fact]
        public void Convert_NonNull_ReturnsTrue()
        {
            var converter = new NotNullConverter();
            var result = converter.Convert("hello", typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void Convert_Null_ReturnsFalse()
        {
            var converter = new NotNullConverter();
            var result = converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void Convert_Object_ReturnsTrue()
        {
            var converter = new NotNullConverter();
            var result = converter.Convert(42, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void ConvertBack_Throws()
        {
            var converter = new NotNullConverter();
            Assert.Throws<NotImplementedException>(() =>
                converter.ConvertBack(true, typeof(object), null, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Instance_IsNotNull()
        {
            Assert.NotNull(NotNullConverter.Instance);
        }

        [Fact]
        public void Instance_IsSameObject()
        {
            Assert.Same(NotNullConverter.Instance, NotNullConverter.Instance);
        }
    }
}
