using System;
using System.Collections.Generic;

namespace Workflows.Abstraction.DTOs
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