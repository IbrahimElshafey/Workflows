# Runner Clustering & Work Partitioning

This document specifies the design for scaling out stateless **Workflow Runners** in a multi-node cluster, ensuring instance-exclusive execution and preventing double-execution concurrency conflicts.

---

## 1. The Scaling Challenge

Although the **Runner** node is logically stateless and communicates via a message broker (or gRPC), multiple active runners concurrently consuming from execution queues can lead to a race condition:

*   **Instance Contention**: Two different runner nodes could pick up concurrent execution requests for the *same* workflow instance (e.g., two signals arriving rapidly in parallel).
*   **State Overwrite**: If two nodes execute different steps of the same workflow in parallel, the database commit that finishes last will overwrite the state of the first, leading to data loss and state corruption.

To prevent this, the engine must guarantee that **a single workflow instance is only executed by one Runner node at any given millisecond**.

---

## 2. Partitioning Strategy: Instance Hashing & Leases

We use **Consistent Hashing** on the `WorkflowInstanceId` to partition active workloads across nodes, managed via a SQL-backed lease registry.

```
Incoming Execution Request (Instance ID Guid)
             │
             ▼
[Hash Function (CRC32)] ──► Modulo Partition Count (e.g., 64)
                                     │
                                     ▼
                       Target Partition ID (0-63)
                                     │
                 ┌───────────────────┴───────────────────┐
                 ▼                                       ▼
        [Runner Node A]                         [Runner Node B]
   (Holds Leases: Partitions 0-31)         (Holds Leases: Partitions 32-63)
```

---

## 3. Database Lease Schema

A central database schema tracks partition ownership.

### The `RunnerLeases` Table
| Column | Type | Description |
| :--- | :--- | :--- |
| **PartitionId** | Int (PK) | Partition index (e.g., `0` to `63`). |
| **OwnerNodeId** | String | Unique identifier of the runner node (e.g., IP address or GUID). |
| **AcquiredAt** | DateTimeOffset | Timestamp when the lease was acquired. |
| **ExpiresAt** | DateTimeOffset | Lease expiration timestamp (usually 10-15 seconds TTL). |

---

## 4. Node Coordination & Failover Lifecycle

### 1. Startup & Lease Acquisition
1.  On startup, a runner node registers its presence and attempts to acquire a subset of partitions from the `RunnerLeases` table by executing an atomic UPDATE query on vacant or expired partitions:
    ```sql
    UPDATE RunnerLeases 
    SET OwnerNodeId = @NodeId, ExpiresAt = @ExpiryTime 
    WHERE PartitionId = @PartitionId AND (OwnerNodeId IS NULL OR ExpiresAt < @NowTime);
    ```
2.  The node starts a background thread to renew its acquired leases every `5 seconds` (before TTL expiration).

### 2. Message Consumption Partitioning
When integrating with message brokers:
*   **RabbitMQ**: Runners subscribe to queues bound using a hashing exchange key based on the `PartitionId`. This ensures the broker routes execution request messages of a partition exclusively to the runner node holding that partition's lease.
*   **Kafka**: Partition hashing maps directly to native Kafka partitions.
*   **gRPC**: The Orchestrator queries `RunnerLeases` to obtain the target runner's address and forwards the RPC request directly.

### 3. Failover (Split-Brain Resolution)
*   If a runner node crashes or suffers a network partition, it will fail to renew its leases.
*   After the lease TTL (e.g., `10 seconds`) expires, healthy runner nodes will detect the expired leases.
*   Healthy nodes dynamically steal/re-acquire the expired partitions and reconfigure their message queue bindings to begin consuming execution requests for the newly acquired partitions.
