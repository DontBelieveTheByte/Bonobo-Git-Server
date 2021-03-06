﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Web.Mvc;
using Bonobo.Git.Server.Configuration;
using Bonobo.Git.Server.Git;
using Bonobo.Git.Server.Git.GitService;
using Bonobo.Git.Server.Security;
using Ionic.Zlib;
using LibGit2Sharp;
using Microsoft.Practices.Unity;

namespace Bonobo.Git.Server.Controllers
{
    [GitAuthorize]
    [RepositoryNameNormalizer("project")]
    public class GitController : Controller
    {
        [Dependency]
        public IRepositoryPermissionService RepositoryPermissionService { get; set; }
        
        [Dependency]
        public IGitService GitService { get; set; }

        public ActionResult SecureGetInfoRefs(String project, String service)
        {
            if (!RepositoryIsValid(project))
            {
                return new HttpNotFoundResult();
            }

            bool allowAnonClone = RepositoryPermissionService.AllowsAnonymous(project);
            bool hasPermission = RepositoryPermissionService.HasPermission(User.Id(), project);
            bool isClone = String.Equals("git-upload-pack", service, StringComparison.OrdinalIgnoreCase);
            bool isPush = String.Equals("git-receive-pack", service, StringComparison.OrdinalIgnoreCase);
            bool allowAnonPush = UserConfiguration.Current.AllowAnonymousPush;

            if (hasPermission || (allowAnonClone && isClone) || (allowAnonPush && isPush))
            {
                return GetInfoRefs(project, service);
            }
            else
            {
                return UnauthorizedResult();
            }
        }

        [HttpPost]
        public ActionResult SecureUploadPack(String project)
        {
            if (!RepositoryIsValid(project))
            {
                return new HttpNotFoundResult();
            }

            if (RepositoryPermissionService.HasPermission(User.Id(), project)
                || RepositoryPermissionService.AllowsAnonymous(project))
            {
                return ExecuteUploadPack(project);
            }
            else
            {
                return UnauthorizedResult();
            }
        }

        [HttpPost]
        public ActionResult SecureReceivePack(String project)
        {
            if (!RepositoryIsValid(project))
            {
                return new HttpNotFoundResult();
            }

            if (RepositoryPermissionService.HasPermission(User.Id(), project)
                || (RepositoryPermissionService.AllowsAnonymous(project) && UserConfiguration.Current.AllowAnonymousPush))
            {
                return ExecuteReceivePack(project);
            }
            else
            {
                return UnauthorizedResult();
            }
        }

        /// <summary>
        /// This is the action invoked if you browse to a .git URL
        /// We just redirect to the repo details page, which is basically what GitHub does
        /// </summary>
        public ActionResult GitUrl(string project)
        {
            return RedirectPermanent(Url.Action("Detail", "Repository", new { id = project}));
        }

        private ActionResult ExecuteReceivePack(string project)
        {
            return new GitCmdResult(
                "application/x-git-receive-pack-result",
                (outStream) =>
                {
                    GitService.ExecuteGitReceivePack(
                        Guid.NewGuid().ToString("N"),
                        project,
                        GetInputStream(disableBuffer: true),
                        outStream);
                });
        }

        private ActionResult ExecuteUploadPack(string project)
        {
            return new GitCmdResult(
                "application/x-git-upload-pack-result",
                (outStream) =>
                {
                    GitService.ExecuteGitUploadPack(
                        Guid.NewGuid().ToString("N"),
                        project,
                        GetInputStream(),
                        outStream);
                });
        }

        private ActionResult GetInfoRefs(String project, String service)
        {
            Response.StatusCode = 200;

            string contentType = String.Format("application/x-{0}-advertisement", service);
            string serviceName = service.Substring(4);
            string advertiseRefsContent = FormatMessage(String.Format("# service={0}\n", service)) + FlushMessage();

            return new GitCmdResult(
                contentType,
                (outStream) =>
                {
                    GitService.ExecuteServiceByName(
                        Guid.NewGuid().ToString("N"),
                        project, 
                        serviceName, 
                        new ExecutionOptions() { AdvertiseRefs = true },
                        GetInputStream(),
                        outStream
                    );
                }, 
                advertiseRefsContent);
        }

        private ActionResult UnauthorizedResult()
        {
            Response.Clear();
            Response.AddHeader("WWW-Authenticate", "Basic realm=\"Bonobo Git\"");
            
            return new HttpStatusCodeResult(401);
        }

        private static String FormatMessage(String input)
        {
            return (input.Length + 4).ToString("X").PadLeft(4, '0') + input;
        }

        private static String FlushMessage()
        {
            return "0000";
        }

        private static DirectoryInfo GetDirectoryInfo(String project)
        {
            return new DirectoryInfo(Path.Combine(UserConfiguration.Current.Repositories, project));
        }

        private static bool RepositoryIsValid(string project)
        {
            var directory = GetDirectoryInfo(project);
            var isValid = Repository.IsValid(directory.FullName);
            return isValid;
        }

        private Stream GetInputStream(bool disableBuffer = false)
        {
            // For really large uploads we need to get a bufferless input stream and disable the max
            // request length.
            Stream requestStream = disableBuffer ?
                Request.GetBufferlessInputStream(disableMaxRequestLength: true) :
                Request.GetBufferedInputStream();

            return Request.Headers["Content-Encoding"] == "gzip" ?
                new GZipStream(requestStream, CompressionMode.Decompress) :
                requestStream;
        }

        protected override void OnException(ExceptionContext filterContext)
        {
            Exception exception = filterContext.Exception;
            Trace.TraceError("{0}: Error caught in GitController {1}", DateTime.Now, exception);
            filterContext.Result = new ContentResult { Content = exception.ToString() };

            filterContext.ExceptionHandled = true;

            filterContext.HttpContext.Response.Clear();
            filterContext.HttpContext.Response.StatusCode = 500;
            filterContext.HttpContext.Response.StatusDescription = "Exception in GitController";
            filterContext.HttpContext.Response.TrySkipIisCustomErrors = true;
        }
    }
}