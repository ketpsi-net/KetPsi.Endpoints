using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KetPsi.Endpoints.Http.Generator;

internal enum PlanKind
{
    Special,
    Route,
    Query,
    Header,
    Form,
    RouteOrQuery,
    Body,
    ServiceOrBody,
    Services,
    KeyedServices,
    BindAsync,
    AsParameters,
}

/// <summary>Effective binding of one value on the request-delegate path.</summary>
internal sealed class ValuePlan
{
    public ValuePlan(ValueModel value, PlanKind kind, string local, string id, int ordinal, bool isMember)
    {
        Value = value;
        Kind = kind;
        Local = local;
        Id = id;
        Ordinal = ordinal;
        IsMember = isMember;
    }

    public ValueModel Value { get; }
    public PlanKind Kind { get; }
    public string Local { get; }
    public string Id { get; }
    public int Ordinal { get; }
    public bool IsMember { get; }
    public DtoModel? Dto { get; set; }
    public List<ValuePlan>? Members { get; set; }
    public string BindName => Value.Binding.Name ?? Value.Name;

    public static bool BodyAllowed(string httpMethod) => httpMethod is "MapPost" or "MapPut" or "MapPatch";

    public static PlanKind Compute(ValueModel v, bool bodyAllowed)
    {
        var t = v.Type;
        if (t.Special != SpecialKind.None)
            return PlanKind.Special;
        switch (v.Binding.Source)
        {
            case BindingSource.Route: return PlanKind.Route;
            case BindingSource.Query: return PlanKind.Query;
            case BindingSource.Header: return PlanKind.Header;
            case BindingSource.Form: return PlanKind.Form;
            case BindingSource.Body: return PlanKind.Body;
            case BindingSource.Services: return PlanKind.Services;
            case BindingSource.KeyedServices: return PlanKind.KeyedServices;
            case BindingSource.AsParameters: return PlanKind.AsParameters;
        }

        if (t.BindAsync != BindAsyncKind.None) return PlanKind.BindAsync;
        if (t.IsParsableScalar) return PlanKind.RouteOrQuery;
        if (t.IsParsableCollection) return bodyAllowed ? PlanKind.ServiceOrBody : PlanKind.Query;
        return PlanKind.ServiceOrBody;
    }
}

/// <summary>
/// Emits the KetPsi request-delegate path for one endpoint:
/// a metadata shim (lambda with the ExecuteAsync signature → MethodInfo for ApiExplorer / filters),
/// a metadata convention (runs first) and a build convention (runs last) that creates the RequestDelegate.
/// </summary>
internal sealed class RequestDelegateEndpoint
{
    private const string Aot = Emitter.Aot;
    private const string Ctx = "__kp_ctx";
    private const string Task = "global::System.Threading.Tasks.Task";

    private readonly EndpointModel _ep;
    private readonly int _n;
    private readonly bool _bodyAllowed;
    private readonly List<ValuePlan> _plans = new();
    private readonly List<string> _buildDecls = new();
    private readonly HashSet<string> _buildDeclSet = new();
    private readonly bool _async;
    private readonly bool _anyForm;
    private readonly bool _formFilesOnly;
    private bool _filtered;

    public RequestDelegateEndpoint(EndpointModel ep, int index)
    {
        _ep = ep;
        _n = index;
        _bodyAllowed = ValuePlan.BodyAllowed(ep.HttpMethod);

        foreach (var p in ep.Parameters)
        {
            var id = p.Ordinal.ToString(CultureInfo.InvariantCulture);
            var plan = new ValuePlan(p.Value, ValuePlan.Compute(p.Value, _bodyAllowed), "__kp_p" + id, id, p.Ordinal, false);
            if (plan.Kind == PlanKind.AsParameters && p.Dto is not null)
            {
                plan.Dto = p.Dto;
                plan.Members = new List<ValuePlan>();
                var j = 0;
                foreach (var m in p.Dto.ConstructorMembers.Concat(p.Dto.PropertyMembers))
                {
                    var mid = id + "_m" + j.ToString(CultureInfo.InvariantCulture);
                    plan.Members.Add(new ValuePlan(m.Value, ValuePlan.Compute(m.Value, _bodyAllowed), "__kp_p" + mid, mid, -1, true));
                    j++;
                }
            }
            _plans.Add(plan);
        }

        var all = AllPlans().ToList();
        _anyForm = all.Any(IsFormPlan);
        _formFilesOnly = all.Where(IsFormPlan).All(p => p.Value.Type.Special is SpecialKind.FormFile or SpecialKind.FormFileCollection);
        _async = _anyForm || all.Any(p => p.Kind is PlanKind.Body or PlanKind.ServiceOrBody or PlanKind.BindAsync);
    }

    private IEnumerable<ValuePlan> AllPlans()
    {
        foreach (var p in _plans)
        {
            if (p.Members is not null)
            {
                foreach (var m in p.Members)
                    yield return m;
            }
            else
            {
                yield return p;
            }
        }
    }

    private static bool IsFormPlan(ValuePlan p)
        => p.Kind == PlanKind.Form
           || (p.Kind == PlanKind.Special && p.Value.Type.Special is SpecialKind.FormFile or SpecialKind.FormFileCollection or SpecialKind.FormCollection);

    private string N => _n.ToString(CultureInfo.InvariantCulture);

    private string Display => Literals.String(_ep.HandlerShortName + ".ExecuteAsync");

    // ------------------------------------------------------------------
    // Registration statement
    // ------------------------------------------------------------------

    public void EmitRegistration(CodeWriter w, string builderVar)
    {
        var verbs = _ep.HttpMethod switch
        {
            "MapPost" => "Post",
            "MapPut" => "Put",
            "MapDelete" => "Delete",
            "MapPatch" => "Patch",
            _ => "Get",
        };
        w.Open();
        w.Line($"var __kp_b = {Aot}.KetPsiEndpoint.Map({builderVar}, {_ep.RouteTemplate}, {Aot}.HttpVerbs.{verbs}, __kp_Metadata_{N});");
        if (_ep.ChainTemplate.Length > 0)
            w.Line($"__kp_b{_ep.ChainTemplate};");
        w.Line($"{Aot}.KetPsiEndpoint.Complete(__kp_b, __kp_Build_{N});");
        w.Close();
    }

    // ------------------------------------------------------------------
    // Members: shim, metadata, build
    // ------------------------------------------------------------------

    public void EmitMembers(CodeWriter w)
    {
        if (!_ep.IsStatic && _ep.Activation == HandlerActivation.Shared)
        {
            // Stateless handler (no instance fields, parameterless constructor): one instance is enough.
            w.Line($"private static readonly {_ep.HandlerFqn} __kp_Handler_{N} = new {_ep.HandlerFqn}();");
            w.Line();
        }

        // Shim: a lambda with the ExecuteAsync parameter list and attributes. Its MethodInfo is endpoint metadata
        // (ApiExplorer / OpenAPI read parameters from it) and EndpointFilterFactoryContext.MethodInfo.
        var shimBody = _ep.Return.Shape == ReturnShape.Void ? "{ }" : "default!";
        w.Line($"private static global::System.Reflection.MethodInfo __kp_Method_{N}()");
        w.Line($"    => ((global::System.Delegate)(static {_ep.Return.DeclaredFqn} ({string.Join(", ", Emitter.LambdaParameters(_ep))}) => {shimBody})).Method;");
        w.Line();

        EmitMetadata(w);
        EmitBuild(w);
    }

    private void EmitMetadata(CodeWriter w)
    {
        w.Open($"private static void __kp_Metadata_{N}(global::Microsoft.AspNetCore.Builder.EndpointBuilder __kp_eb)");
        w.Line($"var __kp_method = __kp_Method_{N}();");
        w.Line("__kp_eb.Metadata.Add(__kp_method);");

        if (_plans.Count > 0)
            w.Line("var __kp_parameters = __kp_method.GetParameters();");
        for (var i = 0; i < _plans.Count; i++)
        {
            var p = _plans[i];
            var model = _ep.Parameters[i];
            if (p.Members is not null && model.Dto is not null)
            {
                var owner = model.UseSurrogate ? Emitter.SurrogateName(_ep, model) : model.Dto.Fqn;
                foreach (var m in p.Members)
                    w.Line($"{Aot}.KetPsiEndpointMetadata.AddAsParametersMember(__kp_eb, __kp_method, typeof({owner}).GetProperty({Literals.String(m.Value.Name)}), {Bool(m.Value.Type.IsParsableScalar)}, {Bool(m.Value.Type.BindAsync != BindAsyncKind.None)}, {Bool(m.Value.IsOptional)});");
            }
            else
            {
                w.Line($"{Aot}.KetPsiEndpointMetadata.AddParameter(__kp_eb, __kp_parameters[{p.Ordinal.ToString(CultureInfo.InvariantCulture)}], {Bool(p.Value.Type.IsParsableScalar)}, {Bool(p.Value.Type.BindAsync != BindAsyncKind.None)}, {Bool(p.Value.IsOptional)});");
            }
        }

        foreach (var p in _plans)
        {
            var t = p.Value.Type;
            if (t.IsParameterMetadataProvider)
                w.Line($"{Aot}.KetPsiEndpointMetadata.PopulateParameter<{t.ValueFqn}>(__kp_parameters[{p.Ordinal}], __kp_eb);");
            if (t.IsMetadataProvider)
                w.Line($"{Aot}.KetPsiEndpointMetadata.Populate<{t.ValueFqn}>(__kp_method, __kp_eb);");
        }

        var needsEnv = AllPlans().Any(p => p.Kind == PlanKind.ServiceOrBody);
        if (needsEnv)
            w.Line($"var __kp_env = {Aot}.EndpointEnvironment.Create(__kp_eb);");
        foreach (var p in AllPlans())
        {
            if (p.Kind == PlanKind.Body)
                w.Line($"{Aot}.KetPsiEndpointMetadata.AddJsonBody(__kp_eb, typeof({TypeOf(p.Value.Type)}), {Bool(p.Value.IsOptional)});");
            else if (p.Kind == PlanKind.ServiceOrBody && _bodyAllowed)
                w.Line($"if (!__kp_env.IsService(typeof({TypeOf(p.Value.Type)}))) {Aot}.KetPsiEndpointMetadata.AddJsonBody(__kp_eb, typeof({TypeOf(p.Value.Type)}), {Bool(p.Value.IsOptional)});");
        }

        if (_anyForm)
            w.Line($"{Aot}.KetPsiEndpointMetadata.AddForm(__kp_eb, {Bool(_formFilesOnly)});");

        var ret = _ep.Return;
        switch (ret.Kind)
        {
            case ResultKind.Json:
                w.Line($"{Aot}.KetPsiEndpointMetadata.AddJsonResponse(__kp_eb, typeof({ret.ResultTypeOf}));");
                break;
            case ResultKind.Object:
                w.Line($"{Aot}.KetPsiEndpointMetadata.AddJsonResponse(__kp_eb, typeof(object));");
                break;
            case ResultKind.String:
                w.Line($"{Aot}.KetPsiEndpointMetadata.AddTextResponse(__kp_eb);");
                break;
            case ResultKind.Result when ret.ResultIsMetadataProvider:
                w.Line($"{Aot}.KetPsiEndpointMetadata.Populate<{ret.ResultTypeOf}>(__kp_method, __kp_eb);");
                break;
        }

        if (_ep.IsObsolete)
            w.Line($"__kp_eb.Metadata.Add(new global::System.ObsoleteAttribute({Literals.String(_ep.ObsoleteMessage)}));");
        w.Close();
        w.Line();
    }

    private static string TypeOf(TypeModel t) => t.IsNullableValueType ? t.Fqn : t.ValueFqn;

    private static string Bool(bool b) => b ? "true" : "false";

    private void EmitBuild(CodeWriter w)
    {
        // Request lambdas are generated first so build-time declarations are collected.
        var unfiltered = new CodeWriter(w.Indent + 2);
        _filtered = false;
        EmitRequestBody(unfiltered);

        var filtered = new CodeWriter(w.Indent + 1);
        _filtered = true;
        EmitRequestBody(filtered);

        w.Open($"private static void __kp_Build_{N}(global::Microsoft.AspNetCore.Builder.EndpointBuilder __kp_eb)");
        w.Line($"var __kp_env = {Aot}.EndpointEnvironment.Create(__kp_eb);");
        foreach (var decl in _buildDecls)
            w.Line(decl);

        var inferred = AllPlans().Where(p => p.Kind == PlanKind.ServiceOrBody).ToList();
        if (inferred.Count > 0)
        {
            var explicitCount = AllPlans().Count(p => p.Kind == PlanKind.Body) + (_anyForm ? 1 : 0);
            var inferredExpr = string.Join(" + ", inferred.Select(p => $"(__kp_svc_{p.Id} ? 0 : 1)"));
            w.Line($"{Aot}.EndpointEnvironment.EnsureBodyInference({inferredExpr}, {explicitCount.ToString(CultureInfo.InvariantCulture)}, {Bool(_bodyAllowed)}, {Display});");
        }

        if (_ep.Return.Kind == ResultKind.Json)
            w.Line($"var __kp_ti_result = __kp_env.GetJsonTypeInfo<{_ep.Return.ResultFqn}>();");

        w.Open("if (__kp_eb.FilterFactories.Count == 0)");
        w.Line($"__kp_eb.RequestDelegate = {(_async ? "async " : string.Empty)}(global::Microsoft.AspNetCore.Http.HttpContext {Ctx}) =>");
        w.Line("{");
        w.Raw(unfiltered.ToString());
        w.Line("};");
        w.Line("return;");
        w.Close();
        w.Line();

        // Filter pipeline: group filters (outermost) → endpoint filters → handler, with the bound arguments.
        w.Line($"var __kp_filtered = {Aot}.KetPsiEndpoint.BuildFilterPipeline(__kp_eb, __kp_Method_{N}(), static (global::Microsoft.AspNetCore.Http.EndpointFilterInvocationContext __kp_fc) =>");
        w.Line("{");
        w.Indent++;
        w.Line($"if (__kp_fc.HttpContext.Response.StatusCode >= 400) return {Aot}.ResultExecutor.EmptyFilterResult;");
        var args = string.Join(", ", _ep.Parameters.Select(p => $"__kp_fc.GetArgument<{p.Value.Type.Fqn}>({p.Ordinal.ToString(CultureInfo.InvariantCulture)})"));
        var call = Call("__kp_fc.HttpContext", args, w);
        switch (_ep.Return.Shape)
        {
            case ReturnShape.Void:
                w.Line(call + ";");
                w.Line($"return {Aot}.ResultExecutor.EmptyFilterResult;");
                break;
            case ReturnShape.Task:
                w.Line($"return {Aot}.ResultExecutor.FromTask({call});");
                break;
            case ReturnShape.ValueTask:
                w.Line($"return {Aot}.ResultExecutor.FromValueTaskVoid({call});");
                break;
            case ReturnShape.TaskOfT:
                w.Line($"return {Aot}.ResultExecutor.FromTaskOf<{_ep.Return.ResultFqn}>({call});");
                break;
            case ReturnShape.ValueTaskOfT:
                w.Line($"return {Aot}.ResultExecutor.FromValueTaskOf<{_ep.Return.ResultFqn}>({call});");
                break;
            default:
                w.Line($"return {Aot}.ResultExecutor.FromValue<{_ep.Return.ResultFqn}>({call});");
                break;
        }
        w.Indent--;
        w.Line("});");
        w.Line($"__kp_eb.RequestDelegate = async (global::Microsoft.AspNetCore.Http.HttpContext {Ctx}) =>");
        w.Line("{");
        w.Raw(filtered.ToString());
        w.Line("};");
        w.Close();
        w.Line();
    }

    private string Call(string httpContextExpr, string args, CodeWriter w)
    {
        if (_ep.IsStatic)
            return $"{_ep.HandlerFqn}.ExecuteAsync({args})";
        switch (_ep.Activation)
        {
            case HandlerActivation.Shared:
                return $"__kp_Handler_{N}.ExecuteAsync({args})";
            case HandlerActivation.New:
                return $"new {_ep.HandlerFqn}().ExecuteAsync({args})";
        }
        w.Line($"var __kp_h = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{_ep.HandlerFqn}>({httpContextExpr}.RequestServices);");
        return $"__kp_h.ExecuteAsync({args})";
    }

    private void Decl(string text)
    {
        if (_buildDeclSet.Add(text))
            _buildDecls.Add(text);
    }

    private string Fail => _filtered ? string.Empty : (_async ? " return;" : $" return {Task}.CompletedTask;");

    // ------------------------------------------------------------------
    // Request body (binding → invoke → write)
    // ------------------------------------------------------------------

    private void EmitRequestBody(CodeWriter w)
    {
        w.Indent++;
        foreach (var p in _plans)
        {
            w.Line($"{p.Value.Type.Fqn} {p.Local} = {p.Value.DefaultLiteral ?? "default!"};");
            if (p.Members is not null)
            {
                foreach (var m in p.Members)
                    w.Line($"{m.Value.Type.Fqn} {m.Local} = {m.Value.DefaultLiteral ?? "default!"};");
            }
        }

        if (_anyForm)
        {
            w.Line("global::Microsoft.AspNetCore.Http.IFormCollection? __kp_form = null;");
            w.Open();
            w.Line($"var __kp_formResult = await {Aot}.ParameterBinder.ReadFormAsync({Ctx}, __kp_env).ConfigureAwait(false);");
            if (!_filtered)
                w.Line("if (!__kp_formResult.Success) return;");
            w.Line("__kp_form = __kp_formResult.Form;");
            w.Close();
        }

        foreach (var p in _plans)
        {
            if (p.Members is not null && p.Dto is not null)
            {
                foreach (var m in p.Members)
                    EmitValue(w, m);
                var ctorArgs = string.Join(", ", p.Members.Take(p.Dto.ConstructorMembers.Length).Select(m => m.Local));
                var props = p.Members.Skip(p.Dto.ConstructorMembers.Length).Select(m => $"@{m.Value.Name} = {m.Local}").ToList();
                var init = props.Count > 0 ? " { " + string.Join(", ", props) + " }" : string.Empty;
                w.Line($"{p.Local} = new {p.Dto.Fqn}({ctorArgs}){init};");
            }
            else
            {
                EmitValue(w, p);
            }
        }

        if (_filtered)
        {
            var count = _plans.Count;
            string create;
            if (count == 0)
                create = $"new global::Microsoft.AspNetCore.Http.DefaultEndpointFilterInvocationContext({Ctx})";
            else if (count <= 8)
                create = $"global::Microsoft.AspNetCore.Http.EndpointFilterInvocationContext.Create<{string.Join(", ", _plans.Select(p => p.Value.Type.Fqn))}>({Ctx}, {string.Join(", ", _plans.Select(p => p.Local))})";
            else
                create = $"new global::Microsoft.AspNetCore.Http.DefaultEndpointFilterInvocationContext({Ctx}, new object?[] {{ {string.Join(", ", _plans.Select(p => p.Local))} }})";
            w.Line($"var __kp_fctx = {create};");
            w.Line("var __kp_result = await __kp_filtered(__kp_fctx).ConfigureAwait(false);");
            w.Line($"await {Aot}.ResultExecutor.ExecuteFilterResultAsync({Ctx}, __kp_result, __kp_env).ConfigureAwait(false);");
            w.Indent--;
            return;
        }

        var call = Call(Ctx, string.Join(", ", _plans.Select(p => p.Local)), w);
        EmitResult(w, call);
        w.Indent--;
    }

    private void EmitResult(CodeWriter w, string call)
    {
        var ret = _ep.Return;
        var r = ret.ResultFqn;
        string WriteValue(string value) => ret.Kind switch
        {
            ResultKind.Result => $"{Aot}.ResultExecutor.ExecuteResultAsync<{r}>({Ctx}, {value})",
            ResultKind.String => $"{Aot}.ResultExecutor.WriteStringAsync({Ctx}, {value})",
            ResultKind.Object => $"{Aot}.ResultExecutor.WriteObjectAsync({Ctx}, {value}, __kp_env.JsonOptions)",
            _ => $"{Aot}.ResultExecutor.WriteJsonAsync<{r}>({Ctx}, {value}, __kp_ti_result)",
        };

        if (_async)
        {
            switch (ret.Shape)
            {
                case ReturnShape.Void:
                    w.Line(call + ";");
                    break;
                case ReturnShape.Task:
                case ReturnShape.ValueTask:
                    w.Line($"await {call}.ConfigureAwait(false);");
                    break;
                case ReturnShape.TaskOfT:
                case ReturnShape.ValueTaskOfT:
                    w.Line($"var __kp_r = await {call}.ConfigureAwait(false);");
                    w.Line($"await {WriteValue("__kp_r")}.ConfigureAwait(false);");
                    break;
                default:
                    w.Line($"await {WriteValue(call)}.ConfigureAwait(false);");
                    break;
            }
            return;
        }

        switch (ret.Shape)
        {
            case ReturnShape.Void:
                w.Line(call + ";");
                w.Line($"return {Task}.CompletedTask;");
                break;
            case ReturnShape.Task:
                w.Line($"return {call};");
                break;
            case ReturnShape.ValueTask:
                w.Line($"return {Aot}.ResultExecutor.FromValueTask({call});");
                break;
            case ReturnShape.TaskOfT:
            case ReturnShape.ValueTaskOfT:
                var vt = ret.Shape == ReturnShape.ValueTaskOfT ? "ValueTask" : "Task";
                var line = ret.Kind switch
                {
                    ResultKind.Result => $"{Aot}.ResultExecutor.ExecuteResult{vt}Async<{r}>({Ctx}, {call})",
                    ResultKind.String => $"{Aot}.ResultExecutor.WriteString{vt}Async({Ctx}, {call})",
                    ResultKind.Object => $"{Aot}.ResultExecutor.WriteObject{vt}Async({Ctx}, {call}, __kp_env.JsonOptions)",
                    _ => $"{Aot}.ResultExecutor.WriteJson{vt}Async<{r}>({Ctx}, {call}, __kp_ti_result)",
                };
                w.Line($"return {line};");
                break;
            default:
                w.Line($"return {WriteValue(call)};");
                break;
        }
    }

    // ------------------------------------------------------------------
    // Values
    // ------------------------------------------------------------------

    private string Required(ValuePlan p, string sourceExpr)
        => $"{{ {Aot}.BindingFailure.Required({Ctx}, __kp_env, {Literals.String(p.Value.Type.DisplayName)}, {Literals.String(p.Value.Name)}, {sourceExpr});{Fail} }}";

    private string Invalid(ValuePlan p, string rawExpr)
        => $"{{ {Aot}.BindingFailure.Invalid({Ctx}, __kp_env, {Literals.String(p.Value.Type.DisplayName)}, {Literals.String(p.Value.Name)}, {rawExpr});{Fail} }}";

    private void EmitValue(CodeWriter w, ValuePlan p)
    {
        var v = p.Value;
        var t = v.Type;
        var name = Literals.String(p.BindName);
        switch (p.Kind)
        {
            case PlanKind.Special:
                EmitSpecial(w, p, name);
                return;

            case PlanKind.Route:
                Decl($"__kp_env.EnsureRouteParameter({name}, {Literals.String(v.Name)});");
                EmitScalar(w, p, $"{Aot}.ParameterBinder.GetRouteValue({Ctx}, {name})", "\"route\"");
                return;

            case PlanKind.RouteOrQuery:
                Decl($"var __kp_route_{p.Id} = __kp_env.HasRouteParameter({name});");
                EmitScalar(w, p,
                    $"(__kp_route_{p.Id} ? {Aot}.ParameterBinder.GetRouteValue({Ctx}, {name}) : (string?){Ctx}.Request.Query[{name}])",
                    $"(__kp_route_{p.Id} ? \"route\" : \"query string\")");
                return;

            case PlanKind.Query:
                if (t.IsParsableCollection)
                    EmitCollection(w, p, $"{Ctx}.Request.Query[{name}]");
                else
                    EmitScalar(w, p, $"(string?){Ctx}.Request.Query[{name}]", "\"query string\"");
                return;

            case PlanKind.Header:
                if (t.IsParsableCollection)
                    EmitCollection(w, p, $"{Ctx}.Request.Headers[{name}]");
                else
                    EmitScalar(w, p, $"(string?){Ctx}.Request.Headers[{name}]", "\"header\"");
                return;

            case PlanKind.Form:
                if (t.IsParsableCollection)
                    EmitCollection(w, p, $"{Aot}.ParameterBinder.GetFormValues(__kp_form, {name})");
                else
                    EmitScalar(w, p, $"{Aot}.ParameterBinder.GetFormValue(__kp_form, {name})", "\"form\"");
                return;

            case PlanKind.Services:
                w.Line($"{p.Local} = {ServiceExpression(t, v.IsOptional, null)};");
                return;

            case PlanKind.KeyedServices:
                w.Line($"{p.Local} = {ServiceExpression(t, v.IsOptional, v.Binding.KeyLiteral ?? "null")};");
                return;

            case PlanKind.Body:
                Decl($"var __kp_ti_{p.Id} = __kp_env.GetJsonTypeInfo<{BodyType(t)}>();");
                EmitBody(w, p);
                return;

            case PlanKind.ServiceOrBody:
                Decl($"var __kp_svc_{p.Id} = __kp_env.IsService(typeof({TypeOf(t)}));");
                Decl($"var __kp_ti_{p.Id} = __kp_svc_{p.Id} ? null! : __kp_env.GetJsonTypeInfo<{BodyType(t)}>();");
                w.Line($"if (__kp_svc_{p.Id})");
                w.Line($"    {p.Local} = {ServiceExpression(t, v.IsOptional, null)};");
                w.Line("else");
                EmitBody(w, p);
                return;

            case PlanKind.BindAsync:
                EmitBindAsync(w, p);
                return;
        }
    }

    private static string BodyType(TypeModel t) => t.IsNullableValueType || t.IsValueType ? t.Fqn : t.ValueFqn;

    private static string ServiceExpression(TypeModel t, bool optional, string? key)
    {
        var type = t.IsNullableValueType ? t.Fqn : t.ValueFqn;
        if (key is null)
        {
            return optional
                ? $"global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<{type}>({Ctx}.RequestServices)"
                : $"global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{type}>({Ctx}.RequestServices)";
        }

        return optional
            ? $"global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetKeyedService<{type}>({Ctx}.RequestServices, {key})"
            : $"global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<{type}>({Ctx}.RequestServices, {key})";
    }

    private void EmitSpecial(CodeWriter w, ValuePlan p, string name)
    {
        var t = p.Value.Type;
        switch (t.Special)
        {
            case SpecialKind.HttpContext: w.Line($"{p.Local} = {Ctx};"); return;
            case SpecialKind.HttpRequest: w.Line($"{p.Local} = {Ctx}.Request;"); return;
            case SpecialKind.HttpResponse: w.Line($"{p.Local} = {Ctx}.Response;"); return;
            case SpecialKind.ClaimsPrincipal: w.Line($"{p.Local} = {Ctx}.User;"); return;
            case SpecialKind.CancellationToken: w.Line($"{p.Local} = {Ctx}.RequestAborted;"); return;
            case SpecialKind.Stream: w.Line($"{p.Local} = {Ctx}.Request.Body;"); return;
            case SpecialKind.PipeReader: w.Line($"{p.Local} = {Ctx}.Request.BodyReader;"); return;
            case SpecialKind.FormFile:
                w.Line($"{p.Local} = {Aot}.ParameterBinder.GetFormFile(__kp_form, {name})!;");
                break;
            case SpecialKind.FormFileCollection:
                w.Line($"{p.Local} = __kp_form?.Files!;");
                break;
            case SpecialKind.FormCollection:
                w.Line($"{p.Local} = __kp_form!;");
                break;
        }

        if (!p.Value.IsOptional)
            w.Line($"if ({p.Local} is null) {Required(p, "\"form\"")}");
    }

    private void EmitScalar(CodeWriter w, ValuePlan p, string rawExpr, string sourceExpr)
    {
        var t = p.Value.Type;
        w.Open();
        w.Line($"var __kp_s = {rawExpr};");
        var missing = t.Parse == ParseKind.String ? "__kp_s is null" : "string.IsNullOrEmpty(__kp_s)";
        if (p.Value.IsOptional)
        {
            w.Line($"if (!({missing}))");
        }
        else
        {
            w.Line($"if ({missing}) {Required(p, sourceExpr)}");
            w.Line("else");
        }

        if (t.Parse == ParseKind.String)
        {
            w.Line($"    {p.Local} = __kp_s;");
        }
        else
        {
            w.Line("{");
            w.Line($"    if ({Parse(t.Parse, t.ValueFqn, "__kp_s", "__kp_v")}) {p.Local} = __kp_v;");
            w.Line($"    else {Invalid(p, "__kp_s")}");
            w.Line("}");
        }
        w.Close();
    }

    private void EmitCollection(CodeWriter w, ValuePlan p, string valuesExpr)
    {
        var t = p.Value.Type;
        w.Open();
        w.Line($"var __kp_sv = {valuesExpr};");
        if (t.Parse == ParseKind.StringValues)
        {
            if (t.IsNullableValueType)
                w.Line($"if (__kp_sv.Count > 0) {p.Local} = __kp_sv;");
            else
                w.Line($"{p.Local} = __kp_sv;");
            w.Close();
            return;
        }

        var element = t.ElementFqn!;
        var isNullableCollection = t.IsReferenceType && p.Value.IsOptional && p.Value.DefaultLiteral is null;
        w.Line("if (__kp_sv.Count == 0)");
        if (isNullableCollection)
        {
            w.Line("{ }");
        }
        else if (t.Collection == CollectionKind.List)
        {
            w.Line($"    {p.Local} = new global::System.Collections.Generic.List<{element}>();");
        }
        else
        {
            w.Line($"    {p.Local} = global::System.Array.Empty<{element}>();");
        }
        w.Line("else");
        w.Open();
        w.Line($"var __kp_arr = new {ArrayOf(element)};");
        w.Open("for (var __kp_i = 0; __kp_i < __kp_sv.Count; __kp_i++)");
        w.Line("var __kp_s = __kp_sv[__kp_i];");
        if (t.Parse == ParseKind.String)
        {
            w.Line("__kp_arr[__kp_i] = __kp_s!;");
        }
        else
        {
            if (t.ElementIsNullable)
                w.Line("if (string.IsNullOrEmpty(__kp_s)) continue;");
            w.Line($"if ({Parse(t.Parse, t.ElementValueFqn!, "__kp_s!", "__kp_v")}) __kp_arr[__kp_i] = __kp_v;");
            w.Line($"else {Invalid(p, "__kp_s")}");
        }
        w.Close();
        w.Line(t.Collection == CollectionKind.List
            ? $"{p.Local} = new global::System.Collections.Generic.List<{element}>(__kp_arr);"
            : $"{p.Local} = __kp_arr;");
        w.Close();
        w.Close();
    }

    /// <summary><c>new T[__kp_sv.Count]</c> for possibly nested array element types (e.g. <c>int?</c>).</summary>
    private static string ArrayOf(string element)
    {
        var bracket = element.IndexOf('[');
        return bracket < 0
            ? $"{element}[__kp_sv.Count]"
            : $"{element.Substring(0, bracket)}[__kp_sv.Count]{element.Substring(bracket)}";
    }

    private static string Parse(ParseKind kind, string valueFqn, string s, string o) => kind switch
    {
        ParseKind.Parsable => $"{Aot}.ParameterBinder.TryParse<{valueFqn}>({s}, out var {o})",
        ParseKind.DateTime => $"{Aot}.ParameterBinder.TryParseDateTime({s}, out var {o})",
        ParseKind.DateTimeOffset => $"{Aot}.ParameterBinder.TryParseDateTimeOffset({s}, out var {o})",
        ParseKind.DateOnly => $"{Aot}.ParameterBinder.TryParseDateOnly({s}, out var {o})",
        ParseKind.TimeOnly => $"{Aot}.ParameterBinder.TryParseTimeOnly({s}, out var {o})",
        ParseKind.Enum => $"{Aot}.ParameterBinder.TryParseEnum<{valueFqn}>({s}, out var {o})",
        ParseKind.Uri => $"{Aot}.ParameterBinder.TryParseUri({s}, out var {o})",
        ParseKind.StaticTryParseWithProvider => $"{valueFqn}.TryParse({s}, global::System.Globalization.CultureInfo.InvariantCulture, out var {o})",
        ParseKind.StaticTryParse => $"{valueFqn}.TryParse({s}, out var {o})",
        _ => "false",
    };

    private void EmitBody(CodeWriter w, ValuePlan p)
    {
        var t = p.Value.Type;
        w.Open();
        w.Line($"var __kp_body = await {Aot}.JsonBodyReader.TryReadAsync<{BodyType(t)}>({Ctx}, __kp_ti_{p.Id}, __kp_env, {Bool(!p.Value.IsOptional)}, {Literals.String(t.DisplayName)}, {Literals.String(p.Value.Name)}).ConfigureAwait(false);");
        if (_filtered)
        {
            w.Line($"if (__kp_body.Success) {p.Local} = __kp_body.Value!;");
        }
        else
        {
            w.Line($"if (!__kp_body.Success){Fail}");
            w.Line($"{p.Local} = __kp_body.Value!;");
        }
        w.Close();
    }

    private void EmitBindAsync(CodeWriter w, ValuePlan p)
    {
        var t = p.Value.Type;
        string call;
        switch (t.BindAsync)
        {
            case BindAsyncKind.Context:
                call = $"{t.ValueFqn}.BindAsync({Ctx})";
                break;
            case BindAsyncKind.ContextAndParameter:
                Decl($"var __kp_pi_{p.Id} = __kp_Method_{N}().GetParameters()[{p.Ordinal.ToString(CultureInfo.InvariantCulture)}];");
                call = $"{t.ValueFqn}.BindAsync({Ctx}, __kp_pi_{p.Id})";
                break;
            default:
                Decl($"var __kp_pi_{p.Id} = __kp_Method_{N}().GetParameters()[{p.Ordinal.ToString(CultureInfo.InvariantCulture)}];");
                call = $"{Aot}.ParameterBinder.BindAsync<{t.ValueFqn}>({Ctx}, __kp_pi_{p.Id})";
                break;
        }

        w.Open();
        w.Line($"var __kp_ba = await {call}.ConfigureAwait(false);");
        var canBeNull = t.IsReferenceType || t.BindAsyncReturnsNullableStruct;
        if (canBeNull)
        {
            var assign = t.BindAsyncReturnsNullableStruct && !t.IsNullableValueType ? "__kp_ba.GetValueOrDefault()" : "__kp_ba";
            if (p.Value.IsOptional)
                w.Line($"if (__kp_ba is not null) {p.Local} = {assign};");
            else
                w.Line($"if (__kp_ba is null) {Required(p, "\"BindAsync\"")} else {p.Local} = {assign};");
        }
        else
        {
            w.Line($"{p.Local} = __kp_ba;");
        }
        w.Close();
    }
}
