using System.IO;
using System.Linq;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Content.Server._Reclamation.Bunkers;

/// <summary>Do not persist runtime links to players, stations, maps or audio streams outside a room.</summary>
internal static class BunkerSnapshotReferences
{
    public static string Clean(string snapshot)
    {
        var yaml = new YamlStream();
        using var reader = new StringReader(snapshot);
        yaml.Load(reader);
        var changed = false;
        foreach (var document in yaml.Documents)
        {
            if (document.RootNode is not YamlMappingNode root
                || !root.Children.TryGetValue(new YamlScalarNode("entities"), out var groupsNode)
                || groupsNode is not YamlSequenceNode groups)
                continue;

            foreach (var group in groups.Children.OfType<YamlMappingNode>())
            {
                if (!group.Children.TryGetValue(new YamlScalarNode("entities"), out var entitiesNode)
                    || entitiesNode is not YamlSequenceNode entities)
                    continue;
                foreach (var entity in entities.Children.OfType<YamlMappingNode>())
                {
                    if (!entity.Children.TryGetValue(new YamlScalarNode("components"), out var componentsNode)
                        || componentsNode is not YamlSequenceNode components)
                        continue;
                    foreach (var component in components.Children.OfType<YamlMappingNode>())
                    {
                        if (!component.Children.TryGetValue(new YamlScalarNode("type"), out var typeNode)
                            || typeNode is not YamlScalarNode type)
                            continue;
                        // Configuration, contents, balances, notes and chat history remain untouched.
                        switch (type.Value)
                        {
                            case "Jukebox":
                                changed |= component.Children.Remove(new YamlScalarNode("audioStream"));
                                break;
                            case "Store":
                                changed |= component.Children.Remove(new YamlScalarNode("startingMap"));
                                if (component.Children.TryGetValue(new YamlScalarNode("boughtEntities"), out var boughtNode)
                                    && boughtNode is YamlSequenceNode bought)
                                {
                                    foreach (var invalid in bought.Children.OfType<YamlScalarNode>()
                                                 .Where(node => node.Value == "invalid").ToArray())
                                    {
                                        bought.Children.Remove(invalid);
                                        changed = true;
                                    }
                                }
                                break;
                            case "NanoChatCartridge":
                                changed |= component.Children.Remove(new YamlScalarNode("station"));
                                changed |= component.Children.Remove(new YamlScalarNode("card"));
                                break;
                            case "UserInterface":
                                changed |= component.Children.Remove(new YamlScalarNode("actors"));
                                break;
                        }
                    }
                }
            }
        }
        if (!changed)
            return snapshot;
        using var writer = new StringWriter();
        yaml.Save(new ExplicitTagEmitter(new Emitter(writer)), false);
        return writer.ToString();
    }
    // RepresentationModel emits tags as implicit by default, which can lose !type: data.
    private sealed class ExplicitTagEmitter(IEmitter inner) : IEmitter
    {
        public void Emit(ParsingEvent ev)
        {
            ev = ev switch
            {
                MappingStart mapping when !mapping.Tag.IsEmpty => new MappingStart(mapping.Anchor, mapping.Tag, false, mapping.Style),
                SequenceStart sequence when !sequence.Tag.IsEmpty => new SequenceStart(sequence.Anchor, sequence.Tag, false, sequence.Style),
                Scalar scalar when !scalar.Tag.IsEmpty => new Scalar(scalar.Anchor, scalar.Tag, scalar.Value, scalar.Style, false, false),
                _ => ev
            };
            inner.Emit(ev);
        }
    }
}
