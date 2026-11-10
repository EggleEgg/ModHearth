using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Stable identity for a UI action: which view owns it, which control inside that view exposes
    /// it, and which routed event triggers it. Compiler-generated lambda names are intentionally
    /// excluded so adding a lambda elsewhere in a constructor does not invalidate the policy.
    /// </summary>
    internal readonly record struct UiActionKey(string RootType, string ControlName, string EventName)
    {
        public override string ToString() => $"{RootType}/{ControlName}.{EventName}";
    }

    /// <summary>
    /// A single app-owned event subscription discovered in a realized logical tree.
    /// </summary>
    internal sealed record DiscoveredUiAction(
        string RootType,
        Interactive Source,
        RoutedEvent RoutedEvent,
        string ControlName,
        string HandlerName)
    {
        public UiActionKey Key => new(RootType, ControlName, RoutedEvent.Name);

        public string DisplayName => $"{Key} -> {HandlerName}";
    }

    /// <summary>
    /// Discovers views and the event subscriptions the application attached to them.
    /// </summary>
    /// <remarks>
    /// Avalonia does not expose its subscription list publicly, so this reads the private
    /// <c>Interactive._eventHandlers</c> field. That is version-brittle by nature: if the field or
    /// the nested subscription shape changes, <see cref="Discover"/> fails loudly instead of
    /// silently reporting zero actions.
    /// </remarks>
    internal static class UiActionDiscovery
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo EventHandlersField =
            typeof(Interactive).GetField("_eventHandlers", InstanceFlags)
            ?? throw new InvalidOperationException(
                "Avalonia's Interactive._eventHandlers field was not found. The UI action smoke test " +
                "needs updating for this Avalonia version.");

        /// <summary>
        /// Returns every concrete <see cref="Window"/> and <see cref="UserControl"/> defined by the
        /// application assembly, so new views cannot be forgotten by a hardcoded list.
        /// </summary>
        public static IReadOnlyList<Type> GetViewTypes(Assembly applicationAssembly)
        {
            return [.. applicationAssembly
                .GetTypes()
                .Where(type => !type.IsAbstract && !type.ContainsGenericParameters)
                .Where(type => typeof(Window).IsAssignableFrom(type) || typeof(UserControl).IsAssignableFrom(type))
                .Where(type => !type.IsNested)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)];
        }

        public static bool HasPublicParameterlessConstructor(Type type) =>
            type.GetConstructor(Type.EmptyTypes) != null;

        /// <summary>
        /// Walks the root's realized logical tree and returns the application's own event handlers.
        /// Framework handlers are ignored because they are not UI actions owned by this codebase.
        /// </summary>
        /// <remarks>
        /// Only realized controls are visible here. Content inside unopened flyouts, context menus,
        /// and item templates that data binding has not created yet is not enumerated; handlers on
        /// those controls still fail the build if they are added through the normal XAML wiring, but
        /// they are not executed by this smoke test.
        /// </remarks>
        public static IReadOnlyList<DiscoveredUiAction> Discover(Control root)
        {
            string rootName = root.GetType().Name;
            Assembly applicationAssembly = root.GetType().Assembly;
            List<DiscoveredUiAction> actions = [];
            HashSet<Interactive> visited = [];

            foreach (Interactive interactive in root.GetLogicalDescendants().OfType<Interactive>().Prepend(root))
            {
                if (!visited.Add(interactive))
                    continue;

                if (EventHandlersField.GetValue(interactive) is not IDictionary handlers)
                    continue;

                string controlName = GetControlName(interactive);

                foreach (DictionaryEntry entry in handlers)
                {
                    if (entry.Key is not RoutedEvent routedEvent || entry.Value is not IEnumerable subscriptions)
                        continue;

                    foreach (object subscription in subscriptions)
                    {
                        if (GetHandler(subscription) is not { } handler)
                            continue;

                        if (handler.Method.DeclaringType?.Assembly != applicationAssembly)
                            continue;

                        actions.Add(new DiscoveredUiAction(
                            rootName,
                            interactive,
                            routedEvent,
                            controlName,
                            FormatHandler(handler)));
                    }
                }
            }

            return actions;
        }

        private static string GetControlName(Interactive interactive)
        {
            if (interactive is Control control && !string.IsNullOrEmpty(control.Name))
                return control.Name;

            return interactive.GetType().Name;
        }

        private static Delegate? GetHandler(object subscription)
        {
            PropertyInfo? handlerProperty = subscription.GetType().GetProperty("Handler", InstanceFlags) ?? throw new InvalidOperationException(
                    $"Avalonia's event subscription type '{subscription.GetType().FullName}' no longer " +
                    "exposes a Handler property. The UI action smoke test needs updating.");
            return handlerProperty.GetValue(subscription) as Delegate;
        }

        private static string FormatHandler(Delegate handler)
        {
            Type? declaringType = handler.Method.DeclaringType;
            if (declaringType == null)
                return handler.Method.Name;

            // Compiler-generated closures render as "<>c__DisplayClass95_0"; keep only the readable
            // tail so reports stay useful without making the identity depend on lambda ordinals.
            string typeName = declaringType.Name;
            if (typeName.Contains(">c", StringComparison.Ordinal))
                typeName = "lambda";

            return $"{typeName}.{handler.Method.Name}";
        }
    }
}
