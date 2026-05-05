using System.Collections.Generic;

namespace Workflows.Abstraction.DTOs.Registration
{
    public class SignalRegistrationInput
    {
        public string SignalIdentifier { get; }

        public SignalRegistrationInput(string signalIdentifier)
        {
            SignalIdentifier = signalIdentifier;
        }

        public List<string> WorkflowsIdentifiers { get; internal set; }
    }
}