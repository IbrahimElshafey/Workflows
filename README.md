## Intro Video in Arabic
[![Intro Video in Arabic](https://img.youtube.com/vi/Oc9NjP0_0ig/0.jpg)](https://www.youtube.com/watch?v=Oc9NjP0_0ig)


* [PDF Intro](https://pdfhost.io/v/DFgolpDyq_Resumable_Workflows)
* [Resumable Workflow Example](#resumable-workflow-example)
* [Why this project?](#why-this-project)
* [**Start using the library NuGet package**](#start-using-the-library)
* [Supported Wait Types](#supported-wait-types)
* [Resumable Workflows UI](https://github.com/IbrahimElshafey/Workflows/blob/main/_Documents/GitHubDocs/Resumable_Workflows_UI.md)
* [Distributed Services and Resumable Workflow](https://github.com/IbrahimElshafey/Workflows/blob/main/_Documents/GitHubDocs/Distributed_Services_and_Resumable_Workflow.md)
* [How to test your resumable workflows?](https://github.com/IbrahimElshafey/Workflows/blob/main/_Documents/GitHubDocs/Testing.md)
* [Configuration](https://github.com/IbrahimElshafey/Workflows/blob/main/_Documents/GitHubDocs/Configuration.md)
* [Database Cleaning Job](https://github.com/IbrahimElshafey/Workflows/blob/main/_Documents/GitHubDocs/Cleaning_Job.md)
* [Samples](https://github.com/IbrahimElshafey/WorkflowsSamples)
* [How it works internally](#how-it-works-internally)

# Resumable Workflow Example
Resumable workflows are workflows or methods endowed with the capability to be paused or suspended during execution when encountering a "wait" method execution request. These workflows remain in a suspended state until the corresponding "wait" method is executed, at which point they seamlessly resume execution from the exact point where they were previously halted. This unique feature allows developers to efficiently manage long-running or asynchronous tasks within their code, offering enhanced readability, maintainability, and flexibility in software development.

**Example**
![WorkflowExample.png](/_Documents/GitHubDocs/IMG/WorkflowExample.png)
Lines Description:
1. A resumable workflow must be defined in a class that inherits from `WorkflowsContainer`.
1. We add the `[WorkflowEntryPoint]` attribute to the resumable workflow to tell the library to register or save the first wait in the database when it scans the DLL for resumable workflows.
1. The resumable workflow must return an `IAsyncEnumerable<Wait>` and must have no input parameters.
1. Each `yield return` statement is a place where the workflow execution can be paused until the required wait is matched, the pause may be days or months.
1. We tell the library that we want to wait for the method `_service.ClientFillsForm` to be executed. This method has an input of type `RegistrationForm` and an output of type `RegistrationResult`.
1. When the `ClientFillsForm` method is executed, the library will evaluate its input and output against the match expression. If the match expression is satisfied, the workflow execution will be resumed. Otherwise, the execution will not be resumed.
1. If we need to capture the input and output of the `ClientFillsForm` method after the match expression is satisfied, we can use the `AfterMatch` method.
* **The library saves the state of the resumable workflow in the database. This includes a serialized instance of the class that contains the resumable workflow, as well as any local variables.**

![PushCallAttribute.png](/_Documents/GitHubDocs/IMG/PushCallAttribute.png)
* The attribute `[PushCall]` must be added to the method you want to wait.
* The method must have one input parameter.
* This attribute will enable the method to push it's input and output to the library when it executed.
# Why this project?
Server processing must be fast to be efficient with processor and memory resources. This means that we can't write a method that blocks for a long time, such as days. For example, the following pseudocode cannot be translated into a single block of code:
```
VactionRequest()
    wait UserSubmitRequest();
    SendRequestToManager();
    wait ManagerResponsetoTheRequest();
    DoSomeStuffAfterManagerResponse();
```
We want to write code that reflects the business requirements so that a developer can hand it off to another developer without needing business documents to understand the code. The source code must be a source of truth about how project parts operate. Handing off a project with hundreds of classes and methods to a new developer doesn't tell them how business flows, but a resumable workflow will simplify understanding of what happens under the hood.

Business workflows/methods must not call each other directly. For example, the method that submits a user request should not call the manager approval method directly. A traditional solution is to create Pub/Sub services that enable the system to be loosely coupled. However, if we used Pub/Sub loosely coupled services, it would be hard to trace what happened without implementing a complex architecture and it will be very hard to get what happen when after an action completed.

This project aims to solve the above problems by using resumable workflows. Resumable workflows are workflows that can be paused and resumed later. This allows us to write code that reflects the business requirements without sacrificing readability. It makes it easier to write distributed systems/SOA/Micro Services that are easy to understand when a developer reads the source code.

This project makes resumable workflows a reality.

# Start using the library
* Create new Web API project, Name it `RequestApproval`
* Check `Enable OpenApi Support`
* Change target framework to '.Net 7.0'
* Install Package
```
Install-package Workflows.AspNetService
```
* In `Program.cs` change:
``` C#
builder.Services.AddControllers();
```
To
```C#
builder.Services
    .AddControllers()
    .AddWorkflows(
        new SqlServerWorkflowsSettings()
        .SetCurrentServiceUrl("<current-service-url>"));
```
* After line `var app = builder.Build();` add line `app.UseWorkflows();`
* This configuration uses LocalDb to store waits data.
* This configuration uses [Hangfire](https://github.com/HangfireIO/Hangfire) for background processing.
* You now can write a resumable workflows in your service.
* See samples [here](https://github.com/IbrahimElshafey/WorkflowsSamples) 

# Supported Wait Types
* Wait **single method** to match (similar to `await` in `async\await`)
``` C#
yield return
    Wait<Project, bool>(ProjectSubmitted, "Project Submitted")
    .MatchIf((input, output) => output == true)
    .AfterMatch((input, output) => CurrentProject = input);
```
* Wait **first method match in a group** of methods (similar to `Task.WhenAny()`)
``` C#
yield return Wait("Wait First In Three",
    Wait<string, string>(Method7, "Method 7"),
    Wait<string, string>(Method8, "Method 8"),
    Wait<string, string>(Method9, "Method 9")
).MatchAny();
```
* Wait **group of methods** to match (similar to `Task.WhenAll()`)
``` C#
yield return Wait("Wait three methods",
    Wait<string, string>(Method1, "Method 1"),
    Wait<string, string>(Method2, "Method 2"),
    Wait<string, string>(Method3, "Method 3")
    );
//or 
yield return Wait("Wait three methods",
    Wait<string, string>(Method1, "Method 1"),
    Wait<string, string>(Method2, "Method 2"),
    Wait<string, string>(Method3, "Method 3")
    ).MatchAll();
```
* **Custom wait for a group** with custom match expression that must be satisfied to mark the group as completed
``` C#
yield return Wait("Wait three methods",
    Wait<string, string>(Method1, "Method 1"),
    Wait<string, string>(Method2, "Method 2"),
    Wait<string, string>(Method3, "Method 3")
)
.MatchIf(waitsGroup => waitsGroup.CompletedCount == 2 && Id == 10 && x == 1);
```
* You can wait a **sub resumable workflow** that is not an entry point
``` C#
 yield return Wait("Wait sub workflow that waits two manager approval.", WaitTwoManagers);
 ....
//method must have  `SubWorkflow` attribute
//Must return `IAsyncEnumerable<Wait>`
[SubWorkflow("WaitTwoManagers")]
public async IAsyncEnumerable<Wait> WaitTwoManagers()
{
	//wait some code
	.
	.
	.
```
* `SubWorkflow` Can wait another `SubWorkflow` 
```C#
[SubWorkflow("SubWorkflow1")]
public async IAsyncEnumerable<Wait> SubWorkflow1()
{
    yield return Wait<string, string>(Method1, "M1").MatchAny();
    yield return Wait("Wait sub workflow2", SubWorkflow2);//this waits another resumable workflow
}
```
* You can wait **mixed group** that contains `SubWorkflow`s, `MethodWait`s and `WaitsGroup`s
```C#
yield return Wait("Wait Many Types Group",
    Wait("Wait three methods in Group",
        Wait<string, string>(Method1, "Method 1"),
        Wait<string, string>(Method2, "Method 2"),
        Wait<string, string>(Method3, "Method 3")
    ),
    Wait("Wait sub workflow", SubWorkflow),
    Wait<string, string>(Method5, "Wait Single Method"));
```
* You can **GoBackTo** a previous wait to wait it again.
``` C#
if (ManagerOneApproval is false)
{
	WriteMessage("Manager one rejected project and replay will go to ManagerOneApproveProject.");
	yield return GoBackTo("ManagerOneApproveProject");//the name must be a previous wait
}
```
* You can **GoBackAfter** a previous wait.
``` C#
yield return
	Wait<Project, bool>(ProjectSumbitted, ProjectSubmitted)
		.MatchIf((input, output) => output == true)
		.AfterMatch((input, output) => CurrentProject = input);

await AskManagerToApprove("Manager 1",CurrentProject.Id);
yield return Wait<ApprovalDecision, bool>("ManagerOneApproveProject", ManagerOneApproveProject)
	.MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
	.AfterMatch((input, output) => ManagerOneApproval == input.Decision);

if (ManagerOneApproval is false)
{
	WriteMessage("Manager one rejected project and replay will go after ProjectSubmitted.");
	yield return GoBackAfter(ProjectSumbitted);//here is go back
}
```
* You can **GoBackBefore** a previous wait
``` C#
WriteMessage("Before project submitted.");
yield return
	Wait<Project, bool>(ProjectSumbitted, ProjectSubmitted)
		.MatchIf((input, output) => output == true && input.IsResubmit == false)
		.AfterMatch((input, output) => CurrentProject = input);

await AskManagerToApprove("Manager 1", CurrentProject.Id);
yield return Wait<ApprovalDecision, bool>("ManagerOneApproveProject", ManagerOneApproveProject)
	.MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
	.AfterMatch((input, output) => ManagerOneApproval == input.Decision);

if (ManagerOneApproval is false)
{
	WriteMessage(
		"ReplayExample: Manager one rejected project and replay will wait ProjectSumbitted again.");
    //here is go back
	yield return
		GoBackBefore<Project, bool>(
			ProjectSumbitted,
			(input, output) => input.Id == CurrentProject.Id && input.IsResubmit == true);
}
```
* You can use **time waits**
``` C#
yield return
    Wait(TimeSpan.FromDays(2), "Wait Two Days")
    .AfterMatch(x => TimeWaitId = x.TimeMatchId);
```

# How it works internally
The library uses an [IAsyncEnumerable](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.iasyncenumerable-1?view=net-7.0) generated state machine to implement a method that can be paused and resumed. An IAsyncEnumerable is a type that provides a sequence of values that can be enumerated asynchronously. A state machine is a data structure that keeps track of the current state of a system. In this case, the state machine keeps track of the current state of where workflow execution reached.

The library saves a serialized object of the class that contains the resumable workflow for each resumable workflow instance. The serialized object is restored when a wait is matched and the workflow is resumed.

One instance created each time the first wait matched for a resumable workflow.

If the match expression is not strict, then a single call may activate multiple waits. However, only one wait will be selected for each workflow. This means that pushed call will activate one instance per workflow, but they can activate multiple instances for different workflows.

