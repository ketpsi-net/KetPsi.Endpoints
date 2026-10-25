using Microsoft.CodeAnalysis;

namespace KetPsi.Endpoints.Grpc.Generator;

internal sealed class ResolvedField
{
    public ResolvedField(FieldModel model, int number)
    {
        Model = model;
        Number = number;
    }

    public FieldModel Model { get; }
    public int Number { get; }
    public string Name => Model.Name;
}

internal enum ConstructionKind
{
    New,
    Factory,
    Unconstructible,
}

internal sealed class ConstructionPlan
{
    public ConstructionKind Kind { get; set; }
    /// <summary>(parameter name, field name) for constructor named arguments / factory positional arguments.</summary>
    public List<(string Param, string Field)> Arguments { get; } = new();
    public List<string> Initializers { get; } = new();
    public string? FactoryTarget { get; set; }
    public string? Reason { get; set; }
}

internal sealed class ResolvedMessage
{
    public ResolvedMessage(MessageModel model) => Model = model;

    public MessageModel Model { get; }
    public string Name { get; set; } = "";
    public List<ResolvedField> Fields { get; } = new();
    public ConstructionPlan Plan { get; set; } = new();
    public bool NeedsEmptyReader { get; set; }
    public string ClrType => Model.ClrType;
    public bool IsReferenceType => Model.IsReferenceType;
}

internal sealed class ResolvedEnum
{
    public ResolvedEnum(EnumModel model) => Model = model;
    public EnumModel Model { get; }
    public string Name { get; set; } = "";
}

internal sealed class ResolvedEndpoint
{
    public ResolvedEndpoint(EndpointModel model) => Model = model;
    public EndpointModel Model { get; }
    public List<ConventionModel> Conventions { get; } = new();
    public List<string> Interceptors { get; } = new();
    public List<BindingModel> Bindings { get; } = new();
}

internal sealed class OutputPlan
{
    public List<ResolvedEndpoint> Endpoints { get; } = new();
    public Dictionary<string, ResolvedMessage> Messages { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ResolvedEnum> Enums { get; } = new(StringComparer.Ordinal);
    public List<Diagnostic> Diagnostics { get; } = new();

    public string MessageName(string key) => Messages[key].Name;
}

/// <summary>Merges call sites and configurations, resolves field numbers, names and construction (pure data).</summary>
internal static class OutputPlanner
{
    public static OutputPlan Build(IReadOnlyList<EndpointModel> endpoints, IReadOnlyList<ConfigResult> configs)
    {
        var plan = new OutputPlan();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        void Report(DiagnosticDescriptor d, LocationInfo? loc, params string[] args)
        {
            var key = d.Id + "|" + string.Join("|", args);
            if (reported.Add(key))
                plan.Diagnostics.Add(DiagnosticInfo.Create(d, loc, args).ToDiagnostic());
        }

        var protoConfigs = configs.SelectMany(c => c.ProtoConfigs).ToList();
        var endpointConfigs = configs.SelectMany(c => c.EndpointConfigs).ToList();

        // ---- endpoints (dedupe identical mappings, reject conflicting ones)
        var byMethod = new Dictionary<string, ResolvedEndpoint>(StringComparer.Ordinal);
        foreach (var ep in endpoints)
        {
            var key = ep.ServiceFullName + "/" + ep.Method;
            if (byMethod.TryGetValue(key, out var existing))
            {
                var e = existing.Model;
                if (e.HandlerType != ep.HandlerType || e.Kind != ep.Kind || e.RequestType != ep.RequestType || e.ResponseType != ep.ResponseType)
                {
                    Report(Diagnostics.DuplicateMapping, ep.Location, ep.ServiceFullName, ep.Method, e.HandlerType.Replace("global::", ""));
                    continue;
                }
                AddEndpointExtras(existing, ep);
                continue;
            }
            var rep = new ResolvedEndpoint(ep);
            AddEndpointExtras(rep, ep);
            foreach (var cfg in endpointConfigs)
            {
                if (cfg.HandlerKey == ep.HandlerType || cfg.ConfigType == ep.ExplicitConfigType)
                {
                    rep.Interceptors.InsertRange(0, cfg.Interceptors.Where(i => !rep.Interceptors.Contains(i)));
                    rep.Bindings.AddRange(cfg.Bindings);
                }
            }
            byMethod[key] = rep;
            plan.Endpoints.Add(rep);
        }

        // Bindings only apply to the endpoint's own request type
        var boundProps = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var ep in plan.Endpoints)
        {
            var own = ep.Bindings.Where(b => b.RequestKey == ep.Model.RequestType)
                .GroupBy(b => b.Property, StringComparer.Ordinal).Select(g => g.Last()).ToList();
            ep.Bindings.Clear();
            ep.Bindings.AddRange(own);
            if (own.Count == 0)
                continue;
            if (!boundProps.TryGetValue(ep.Model.RequestType, out var set))
                boundProps[ep.Model.RequestType] = set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in own)
                set.Add(b.Property);
        }

        // ---- message / enum universe
        var allMessages = new Dictionary<string, MessageModel>(StringComparer.Ordinal);
        var messageLocation = new Dictionary<string, LocationInfo?>(StringComparer.Ordinal);
        var allEnums = new Dictionary<string, EnumModel>(StringComparer.Ordinal);
        foreach (var ep in plan.Endpoints)
        {
            foreach (var m in ep.Model.Messages)
            {
                if (!allMessages.ContainsKey(m.Key))
                {
                    allMessages[m.Key] = m;
                    messageLocation[m.Key] = ep.Model.Location;
                }
            }
            foreach (var en in ep.Model.Enums)
            {
                if (!allEnums.ContainsKey(en.Key))
                    allEnums[en.Key] = en;
            }
        }

        var closedConfigs = new Dictionary<string, ProtoTypeConfigModel>(StringComparer.Ordinal);
        var openConfigs = new Dictionary<string, ProtoTypeConfigModel>(StringComparer.Ordinal);
        foreach (var c in protoConfigs)
        {
            var target = c.IsOpenGeneric ? openConfigs : closedConfigs;
            target[c.TargetKey] = target.TryGetValue(c.TargetKey, out var prev)
                ? prev with { Fields = prev.Fields.Concat(c.Fields).ToEquatableArray(), Factory = c.Factory ?? prev.Factory }
                : c;
        }

        // ---- resolve fields (all known messages; only reachable ones are emitted)
        var resolved = new Dictionary<string, ResolvedMessage>(StringComparer.Ordinal);
        var factories = new Dictionary<string, FactoryModel>(StringComparer.Ordinal);
        foreach (var m in allMessages.Values)
        {
            var rm = new ResolvedMessage(m);
            resolved[m.Key] = rm;
            boundProps.TryGetValue(m.Key, out var excluded);
            ProtoTypeConfigModel? cfg = null;
            if (!closedConfigs.TryGetValue(m.Key, out cfg) && m.OpenGenericKey.Length > 0)
                openConfigs.TryGetValue(m.OpenGenericKey, out cfg);
            if (cfg?.Factory is not null)
                factories[m.Key] = cfg.Factory;

            var candidates = m.Fields.Where(f => excluded is null || !excluded.Contains(f.Name)).ToList();
            var display = Display(m.ClrType);
            if (cfg is not null && cfg.Fields.Length > 0)
            {
                var byName = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var cf in cfg.Fields)
                {
                    if (m.Fields.All(f => f.Name != cf.Name))
                    {
                        Report(Diagnostics.ConfiguredPropertyNotFound, cf.Location ?? cfg.Location, Display(cfg.ConfigType), cf.Name, display);
                        continue;
                    }
                    byName[cf.Name] = cf.Number;
                }
                foreach (var f in candidates)
                {
                    if (byName.TryGetValue(f.Name, out var n))
                        rm.Fields.Add(new ResolvedField(f, n));
                    else
                        rm.UnmappedReason(f, "not configured in " + Display(cfg.ConfigType), messageLocation[m.Key], Report);
                }
            }
            else if (candidates.Any(f => f.AttributeNumber is not null))
            {
                foreach (var f in candidates)
                {
                    if (f.AttributeNumber is { } n)
                        rm.Fields.Add(new ResolvedField(f, n));
                    else
                        rm.UnmappedReason(f, "no [ProtoMember] attribute", messageLocation[m.Key], Report);
                }
            }
            else
            {
                foreach (var f in candidates)
                    rm.Fields.Add(new ResolvedField(f, f.Order + 1));
            }
            rm.Fields.Sort((a, b) => a.Number.CompareTo(b.Number));
        }

        // ---- reachability from RPC roots
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var reachableEnums = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        foreach (var ep in plan.Endpoints)
        {
            if (ep.Model.RequestType.Length > 0) stack.Push(ep.Model.RequestType);
            if (ep.Model.ResponseType.Length > 0) stack.Push(ep.Model.ResponseType);
        }
        while (stack.Count > 0)
        {
            var key = stack.Pop();
            if (!reachable.Add(key) || !resolved.TryGetValue(key, out var rm))
                continue;
            foreach (var f in rm.Fields)
            {
                foreach (var v in new[] { f.Model.Value, f.Model.MapKey })
                {
                    if (v is null) continue;
                    if (v.Kind == ValueKind.Message) stack.Push(v.RefKey);
                    if (v.Kind == ValueKind.Enum) reachableEnums.Add(v.RefKey);
                }
            }
        }

        // ---- validate reachable messages
        foreach (var key in reachable.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!resolved.TryGetValue(key, out var rm))
                continue;
            var loc = messageLocation[key];
            var display = Display(rm.ClrType);
            var valid = new List<ResolvedField>();
            var seen = new Dictionary<int, string>();
            foreach (var f in rm.Fields)
            {
                if (f.Model.UnsupportedReason is { } reason)
                {
                    Report(Diagnostics.UnsupportedPropertyType, loc, display, f.Name, reason);
                    continue;
                }
                if (f.Number < 1 || f.Number > 536_870_911 || f.Number is >= 19000 and <= 19999)
                {
                    Report(Diagnostics.InvalidFieldNumber, loc, f.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), display, f.Name);
                    continue;
                }
                if (seen.TryGetValue(f.Number, out var other))
                {
                    Report(Diagnostics.DuplicateFieldNumber, loc, f.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), other, f.Name, display);
                    continue;
                }
                if (f.Model.Shape == FieldShape.Map && !IsValidMapKey(f.Model.MapKey!.Kind))
                {
                    Report(Diagnostics.InvalidMapKey, loc, display, f.Name, Display(f.Model.MapKey.ClrType));
                    continue;
                }
                seen[f.Number] = f.Name;
                valid.Add(f);
            }
            rm.Fields.Clear();
            rm.Fields.AddRange(valid);
            if (rm.Fields.Count > 0 && rm.Model.Fields.All(x => x.AttributeNumber is null)
                && !closedConfigs.ContainsKey(key) && !(rm.Model.OpenGenericKey.Length > 0 && openConfigs.ContainsKey(rm.Model.OpenGenericKey)))
                Report(Diagnostics.ImplicitFieldNumbers, loc, display);

            factories.TryGetValue(key, out var factory);
            rm.Plan = PlanConstruction(rm, factory);
            if (rm.Plan.Kind == ConstructionKind.Unconstructible)
                Report(Diagnostics.CannotConstruct, loc, display, rm.Plan.Reason ?? "no usable constructor");
            else
            {
                var covered = new HashSet<string>(rm.Plan.Arguments.Select(a => a.Field).Concat(rm.Plan.Initializers), StringComparer.Ordinal);
                foreach (var f in rm.Fields.Where(f => !covered.Contains(f.Name)))
                    Report(Diagnostics.PropertyNotSettable, loc, display, f.Name);
            }

            foreach (var f in rm.Fields)
            {
                if (f.Model.Shape == FieldShape.Map && f.Model.Value.Kind == ValueKind.Message && !f.Model.Value.IsValueType
                    && resolved.TryGetValue(f.Model.Value.RefKey, out var mv))
                    mv.NeedsEmptyReader = true;
            }
            plan.Messages[key] = rm;
        }

        foreach (var key in reachableEnums)
        {
            if (!allEnums.TryGetValue(key, out var en))
                continue;
            if (en.OutOfRangeMember is { } bad)
                Report(Diagnostics.EnumValueOutOfRange, null, Display(en.ClrType), bad);
            plan.Enums[key] = new ResolvedEnum(en);
        }

        AssignNames(plan);
        return plan;
    }

    private static void UnmappedReason(this ResolvedMessage rm, FieldModel f, string reason, LocationInfo? loc,
        Action<DiagnosticDescriptor, LocationInfo?, string[]> report)
        => report(Diagnostics.PropertyNotSerialized, loc, new[] { Display(rm.ClrType), f.Name, reason });

    private static void AddEndpointExtras(ResolvedEndpoint target, EndpointModel ep)
    {
        if (ep.Conventions is not null && !target.Conventions.Contains(ep.Conventions))
            target.Conventions.Add(ep.Conventions);
        foreach (var i in ep.InlineInterceptors)
        {
            if (!target.Interceptors.Contains(i))
                target.Interceptors.Add(i);
        }
        target.Bindings.AddRange(ep.InlineBindings);
    }

    public static bool IsValidMapKey(ValueKind kind)
        => kind is ValueKind.Int32 or ValueKind.Int64 or ValueKind.UInt32 or ValueKind.UInt64 or ValueKind.Bool
            or ValueKind.String or ValueKind.Char or ValueKind.Guid or ValueKind.Decimal;

    public static string Display(string clr) => clr.Replace("global::", "");

    private static ConstructionPlan PlanConstruction(ResolvedMessage rm, FactoryModel? factory)
    {
        var plan = new ConstructionPlan();
        var fields = rm.Fields;
        var fieldNames = new HashSet<string>(fields.Select(f => f.Name), StringComparer.Ordinal);

        if (factory is not null)
        {
            plan.Kind = ConstructionKind.Factory;
            plan.FactoryTarget = (factory.DeclaringType ?? rm.ClrType) + "." + RouteResolver.EscapeKeyword(factory.MethodName);
            var byName = factory.ParameterNames.Length > 0
                ? factory.ParameterNames.Select(p => fields.FirstOrDefault(f => string.Equals(f.Name, p, StringComparison.OrdinalIgnoreCase))).ToList()
                : null;
            if (byName is not null && byName.All(f => f is not null))
            {
                foreach (var f in byName)
                    plan.Arguments.Add(("", f!.Name));
            }
            else
            {
                foreach (var f in fields)
                    plan.Arguments.Add(("", f.Name));
            }
            return plan;
        }

        var required = rm.Model.Fields.Where(f => f.IsRequired).Select(f => f.Name).ToList();
        CtorModel? best = null;
        var bestCoverage = -1;
        var bestParams = int.MaxValue;
        foreach (var ctor in rm.Model.Constructors)
        {
            var viable = true;
            var ctorFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in ctor.Parameters)
            {
                if (p.MatchedProperty.Length > 0 && fieldNames.Contains(p.MatchedProperty))
                    ctorFields.Add(p.MatchedProperty);
                else if (!p.HasDefault)
                {
                    viable = false;
                    break;
                }
            }
            if (!viable)
                continue;
            if (!ctor.SetsRequiredMembers)
            {
                foreach (var r in required)
                {
                    var rf = fields.FirstOrDefault(f => f.Name == r);
                    if (rf is null || rf.Model.Setter == SetterKind.None)
                    {
                        viable = false;
                        break;
                    }
                }
                if (!viable)
                    continue;
            }
            var coverage = fields.Count(f => ctorFields.Contains(f.Name) || f.Model.Setter != SetterKind.None);
            if (coverage > bestCoverage || (coverage == bestCoverage && ctor.Parameters.Length < bestParams))
            {
                best = ctor;
                bestCoverage = coverage;
                bestParams = ctor.Parameters.Length;
            }
        }

        if (best is null)
        {
            plan.Kind = ConstructionKind.Unconstructible;
            plan.Reason = rm.Model.Constructors.Length == 0
                ? "it has no accessible constructor"
                : "no accessible constructor can be satisfied from the serialized properties (or required members are not settable)";
            return plan;
        }

        plan.Kind = ConstructionKind.New;
        var inCtor = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in best.Parameters)
        {
            if (p.MatchedProperty.Length > 0 && fieldNames.Contains(p.MatchedProperty))
            {
                plan.Arguments.Add((p.Name, p.MatchedProperty));
                inCtor.Add(p.MatchedProperty);
            }
        }
        foreach (var f in fields)
        {
            if (!inCtor.Contains(f.Name) && f.Model.Setter != SetterKind.None)
                plan.Initializers.Add(f.Name);
        }
        return plan;
    }

    // ---------------------------------------------------------------- naming

    private static void AssignNames(OutputPlan plan)
    {
        var entries = new List<(string Key, string BaseName, string Namespace, Action<string> Set)>();
        foreach (var m in plan.Messages.Values)
            entries.Add((m.Model.Key, Sanitize(m.Model.BaseName), m.Model.Namespace, n => m.Name = n));
        foreach (var e in plan.Enums.Values)
            entries.Add((e.Model.Key, Sanitize(e.Model.BaseName), e.Model.Namespace, n => e.Name = n));

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in entries.GroupBy(e => e.BaseName, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var items = group.OrderBy(i => i.Key, StringComparer.Ordinal).ToList();
            if (items.Count == 1 && used.Add(items[0].BaseName))
            {
                items[0].Set(items[0].BaseName);
                continue;
            }

            // Disambiguate with the shortest namespace suffix that makes all names unique
            var nsParts = items.Select(i => i.Namespace.Length == 0 ? Array.Empty<string>() : i.Namespace.Split('.')).ToList();
            var maxDepth = nsParts.Max(p => p.Length);
            string[]? chosen = null;
            for (var depth = 1; depth <= maxDepth && chosen is null; depth++)
            {
                var names = items.Select((i, idx) =>
                {
                    var parts = nsParts[idx];
                    var suffix = parts.Skip(Math.Max(0, parts.Length - depth));
                    return Sanitize(string.Join("_", suffix.Concat(new[] { i.BaseName })));
                }).ToArray();
                if (names.Distinct(StringComparer.Ordinal).Count() == names.Length && names.All(n => !used.Contains(n)))
                    chosen = names;
            }
            chosen ??= items.Select(i => i.BaseName + "_" + CallSiteParser.StableHash(i.Key).Substring(0, 8)).ToArray();
            for (var i = 0; i < items.Count; i++)
            {
                var name = chosen[i];
                while (!used.Add(name))
                    name += "_";
                items[i].Set(name);
            }
        }
    }

    public static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Message";
        var chars = name.Select(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' ? c : '_').ToArray();
        var s = new string(chars);
        return char.IsDigit(s[0]) ? "_" + s : s;
    }
}
