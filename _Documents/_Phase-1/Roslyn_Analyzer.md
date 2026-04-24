# Validate attributes usage
* WorkflowsContainer must be constructor less if you want to pass dependancies create a method `SetDependencies`
* [PushCallAttribute] must applied to a method you want to wait
* [PushCallAttribute] 
	* method must have one input that is serializable
	* MethodUrn must not be empty if attribute applied
* Validate input output type serialization
* Resumable Workflow URN name must not duplicate
* Method URN name must not duplicate in same class
* You don't need to set Signal attribute on interface method

# Validate Wait Requests
* You didn't set the `MatchExpression` for wait
* You didn't set the `AfterMatchAction` for wait
* When the replay type is [XX_NewMatch],the wait to replay  must be of type [MethodWait]
* Go to the first wait with same match will create new separate workflow instance
* Go before the first wait with same match will create new separate workflow instance.
* Replay Go Before found no waits!!
* The wait named [{Name}] is duplicated in workflow body