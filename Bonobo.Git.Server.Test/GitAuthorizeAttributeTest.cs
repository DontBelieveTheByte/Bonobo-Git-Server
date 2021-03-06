﻿using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bonobo.Git.Server.Test
{
    [TestClass]
    public class GitAutherizeAttributeTest
    {
        [TestMethod]
        public void GetRepoPathTest()
        {
            var repo = GitAuthorizeAttribute.GetRepoPath("/other/test.git/info/refs", "/other");
            Assert.AreEqual("test", repo);
            repo = GitAuthorizeAttribute.GetRepoPath("/test.git/info/refs", "/");
            Assert.AreEqual("test", repo);
        }
    }
}
