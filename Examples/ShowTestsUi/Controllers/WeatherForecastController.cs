using Microsoft.AspNetCore.Mvc;
using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;

namespace ShowTestsUi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {


        private readonly ILogger<WeatherForecastController> _logger;

        public WeatherForecastController(ILogger<WeatherForecastController> logger)
        {
            _logger = logger;
        }

        [HttpGet("CallAction")]
        public string CallAction(int methodId)
        {
            switch (methodId)
            {
                case 1: return new SequenceWorkflow().Method1("M1");
                case 2: return new SequenceWorkflow().Method2("M2");
                case 3: return new SequenceWorkflow().Method3("M13");
            }
            return null;
        }
    }

    public class SequenceWorkflow : WorkflowContainer
    {
        [Workflow("ThreeMethodsSequence")]
        public async IAsyncEnumerable<Wait> ThreeMethodsSequence()
        {
            int x = 1;
            yield return WaitMethod<string, string>(Method1, "M1")
            //.AfterMatch((_, _) => x++);
            ;
            x++;
            if (x != 2)
                throw new Exception("Closure not continue");
            x++;
            yield return WaitMethod<string, string>(Method2, "M2").MatchAny();
            x++;
            if (x != 4)
                throw new Exception("Closure not continue");
            x++;
            yield return WaitMethod<string, string>(Method3, "M3").MatchAny();
            x++;
            if (x != 6)
                throw new Exception("Closure not continue");
            await Task.Delay(100);
        }

        [EmitSignal("RequestAdded")] public string Method1(string input) => input + "M1";
        [EmitSignal("Method2")] public string Method2(string input) => input + "M2";
        [EmitSignal("Method3")] public string Method3(string input) => input + "M3";
    }
}