using CVRFury.Builder;
using NUnit.Framework;

namespace CVRFury.Tests
{
    public class ParamNameAllocatorTests
    {
        [Test]
        public void Sanitize_ReplacesInvalidCharsWithUnderscore()
        {
            Assert.AreEqual("Clothing_Hat", ParamNameAllocator.Sanitize("Clothing/Hat"));
            Assert.AreEqual("a_b_c_", ParamNameAllocator.Sanitize("a b.c!"));
        }

        [Test]
        public void Sanitize_KeepsLettersDigitsUnderscore()
        {
            Assert.AreEqual("Toggle_1", ParamNameAllocator.Sanitize("Toggle_1"));
        }

        [Test]
        public void Allocate_ReturnsRequestedNameWhenFree()
        {
            var a = new ParamNameAllocator();
            Assert.AreEqual("Hat", a.Allocate("Hat"));
        }

        [Test]
        public void Allocate_DeduplicatesCollisions()
        {
            var a = new ParamNameAllocator();
            Assert.AreEqual("Hat", a.Allocate("Hat"));
            Assert.AreEqual("Hat_1", a.Allocate("Hat"));
            Assert.AreEqual("Hat_2", a.Allocate("Hat"));
        }

        [Test]
        public void Allocate_GeneratesNameWhenBlank()
        {
            var a = new ParamNameAllocator();
            var name = a.Allocate("");
            Assert.IsNotEmpty(name);
            Assert.AreNotEqual(name, a.Allocate("")); // unique each time
        }

        [Test]
        public void Allocate_SanitizesBeforeDeduplicating()
        {
            var a = new ParamNameAllocator();
            Assert.AreEqual("Clothing_Hat", a.Allocate("Clothing/Hat"));
            Assert.AreEqual("Clothing_Hat_1", a.Allocate("Clothing.Hat"));
        }
    }
}
