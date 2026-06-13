using CVRFury.Builder;
using NUnit.Framework;
using UnityEngine;

namespace CVRFury.Tests
{
    public class HierarchyUtilTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("Root");

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void GetPath_RootReturnsEmptyString()
        {
            Assert.AreEqual("", HierarchyUtil.GetPath(_root.transform, _root.transform));
        }

        [Test]
        public void GetPath_DirectChild()
        {
            var child = new GameObject("Hat");
            child.transform.SetParent(_root.transform);
            Assert.AreEqual("Hat", HierarchyUtil.GetPath(_root.transform, child.transform));
        }

        [Test]
        public void GetPath_NestedChild()
        {
            var armature = new GameObject("Armature");
            armature.transform.SetParent(_root.transform);
            var head = new GameObject("Head");
            head.transform.SetParent(armature.transform);
            Assert.AreEqual("Armature/Head", HierarchyUtil.GetPath(_root.transform, head.transform));
        }

        [Test]
        public void GetPath_NonDescendantReturnsNull()
        {
            var outsider = new GameObject("Outsider");
            Assert.IsNull(HierarchyUtil.GetPath(_root.transform, outsider.transform));
            Object.DestroyImmediate(outsider);
        }
    }
}
