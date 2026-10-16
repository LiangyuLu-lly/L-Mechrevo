using System.Collections.Generic;

namespace MechrevoLite;

internal sealed record ModelScopeMigration(bool Migrated, string Model, IReadOnlyList<string> RemovedKeys, IReadOnlyList<KeyValuePair<string, object>> ScopedValues);
