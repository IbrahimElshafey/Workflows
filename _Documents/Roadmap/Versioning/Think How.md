I now think the best approach is to use a source generator that automatically generates the workflow schema and archives the current workflow version whenever it detects that the workflow version number has been incremented.

To achieve this, the generator should:

1. Automatically copy the current workflow class into a versioned archive folder within the same project, for example:

   `Workflows\Archive\WorkflowName\Vxx\WorkflowName_Vxx.cs`

   The generated archive file should have its **Build Action** set to **None**.

2. Generate a file named:

   `WorkflowName_Vxx_Schema.json`

   This file should contain the complete workflow definition, including:

   * The workflow class JSON schema
   * State machine JSON schema
   * Workflow graph definition
   * Signal schemas
   * Command class schemas
   * Wait-state class schemas

   All of these artifacts should be embedded within the main workflow schema document.

3. Generate a migration layout class from the schema above, for example:

   `WorkflowNameVxx_Layout.cs`

   This class will be used by the migration  to map and migrate workflow instances between versions.
