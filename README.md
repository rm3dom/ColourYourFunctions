# Colour your functions - a transaction experiment

TLDR C#: [Tx.cs](DotNet/Tx/Tx/Tx.cs)

# Atomicity

Atomicity is a core concept in database systems: a transaction must either complete entirely or not run at all.
If anything fails midway, the whole transaction is rolled back as if it never happened.

We need guarantees that database operations are atomic and retryable. It's extremely hard to spot logic that breaks
these guarantees, so we should lean on the compiler and tests to help us.

# Guarantees

Compile-time guarantees are preferred over runtime guarantees. Below is a list of guarantees with short codes we can
reference in the code:

* tx-atom
* tx-repeat
* tx-never
* tx-never-nest
* tx-read
* tx-write

### tx-atom

A group of database operations (a transaction) is atomic.

### tx-repeat

A group of database operations (a transaction) is retryable/repeatable.

On a deadlock or connection error, the transaction can be safely retried. All operations within the transaction scope
are either rolled back or idempotent (preferably rolled back rather than relying on idempotency).

### tx-never

No transaction in the call stack.

A function may never be called within a transaction. Long-running operations should not be executed inside a
transaction. Likewise, a function that starts its own write transaction should not be invoked within another
transaction (see tx-never-nest).

### tx-never-nest

Only one transaction in the call stack.

* A transaction may not be nested when its a write.
* A transaction may not be nested when the outer transaction is a write and the inner transaction has a ReadCommitted
  isolation level or higher.

Note: we do not refer to database-managed nested transactions, but rather to starting a new transaction within another
transaction's scope.

### tx-read

Read-only in the call stack.

All functions called as part of a read transaction must be read-only, including all calls up the stack. A function's
signature should make it clear that it is read-only and that the functions it calls are also read-only.

### tx-write

Read/write in the call stack.

All functions called as part of a write transaction may read or write.

# Enforcement

| Gaurantee     | Enforcement                                   |
|---------------|-----------------------------------------------|
| tx-atom       | Runtime assertion / Testing                   |
| tx-repeat     | Runtime assertion / Testing                   |
| tx-never      | Runtime assertion / Testing / Compiler Plugin |
| tx-never-nest | Runtime assertion / Testing / Compiler Plugin |
| tx-read       | Compile time                                  |
| tx-write      | Compile time                                  |

Some compile-time guarantees are provided by colouring functions with their transaction participation/intent.
Repeatability is verified by forcing retries at runtime (see Testing).
Most guarantees—except tx-repeat and tx-atom could be provided by a compiler plugin.

# Testing

Testing is done by forcing retries at runtime. For example, every time a transaction is started, force at least two
rollback-and-retry cycles. This validates the tx-repeat guarantee.

Example:

```
fun myFunc() {
    txWrite {
        // This code will be retried at least twice.
        // A rollback is forced when the lambda returns, and then the block is rerun.
    }
}
```

# Structuring your code

Typically, you should structure your functions so that reads happen first and writes at the end, although this is not
always possible. For example:

```
fun myFunc() {
    txRead {
        ...
    }
    
    aLongRunningOperation() //tx-never
    
    txWrite {
        ...
    }
}
```

### Using the saga pattern

When it's not possible to structure your code in a read-first, write-last fashion, you can use the saga pattern.

```
fun myFunc() {
    saga {
       saga(
          do = { 
             txWrite {
                ...
             }
          }, 
          undo = {
             txWrite {
                ...
             }
          }
       )
       saga(
          do = { 
             callCreateApi()
          }, 
          undo = {
             callDeleteApi()
          }
       )
       saga(
          do = { 
             txWrite {
                ...
             }
          }, 
          undo = {
             txWrite {
                ...
             }
          }
       )
    }.go()
}
```

The same can be achieved using try-catch-finally blocks.

```
fun myFunc() {
    try {
      txWrite {
         do...
      }
      txWrite {
         do...
      }
    } catch(_: Exception) {
      txWrite {
         undo...
      }
    }
}
```

# Colouring

It's only "colouring" if your function can work without a transaction. A function cannot guarantee
atomicity without a transaction—so that's not colouring.
