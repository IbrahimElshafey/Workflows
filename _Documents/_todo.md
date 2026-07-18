* Don't load wait tree in all cases , only when needed, I think we can do this.
* Root workflows could be used as sub-workflows, but we should be careful to avoid circular references.
* We should have unsafe mode where we didn't save command paylaod to DB and state but to memory, this is useful for workflows that need fast execution.