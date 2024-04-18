using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LibHelpers
{
    /// <summary>
    /// Created especially to intercept calls and log them
    /// </summary>
    public class ActionInterceptor : ActionFilterAttribute
    {

        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            try
            {
                //sign start-time
                context.HttpContext.Items["StartTime"] = DateTime.UtcNow;

                // Log the action arguments
                log.Trace($"Action " +
                    $"Name: {context.ActionDescriptor.DisplayName}, " +
                    $"Args -> {JsonSerializer.Serialize(context.ActionArguments)}");
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        public override void OnActionExecuted(ActionExecutedContext context)
        {
            try
            {
                //get signed start-time
                DateTime startTime = (DateTime)context.HttpContext.Items["StartTime"];

                // Log the action result
                log.Trace($"Action " +
                    $"Name: {context.ActionDescriptor.DisplayName}, " +
                    $"Result -> {JsonSerializer.Serialize(context.Result)} " +
                    $"ElapsedTime -> {(DateTime.UtcNow - startTime).TotalMilliseconds} ms");
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        //public override Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        //{
        //    return base.OnActionExecutionAsync(context, next);
        //}

        //public override void OnResultExecuting(ResultExecutingContext context)
        //{
        //    base.OnResultExecuting(context);
        //}

        //public override void OnResultExecuted(ResultExecutedContext context)
        //{
        //    base.OnResultExecuted(context);
        //}

        //public override Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        //{
        //    return base.OnResultExecutionAsync(context, next);
        //}
    }
}
