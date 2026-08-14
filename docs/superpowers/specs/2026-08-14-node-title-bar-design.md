# Node Title Bar and Drag Region Design

**Date:** 2026-08-14
**Branch:** feature/node-layout-resize

## Goal

Give every flow node a visible title bar that displays the node title and provides an obvious area for moving the node, while preserving the existing left-input/right-output layout and stretchable content behavior.

## Scope

This change includes:

1. Add a title bar to the NodeView default template above the socket/content row.
2. Display the existing NodeModel.Name value in that title bar.
3. Make the complete title bar a clear drag surface and indicate that behavior with a move cursor.
4. Remove the duplicate title text from ordinary and image-preview content roots.
5. Keep the existing FlowCanvas drag state machine, selection behavior, socket hit testing, resize thumb, and content factory API unchanged.
6. Add regression tests for the template contract, title binding, drag-surface properties, and content roots.

This change does not add node renaming, a new title dependency property, a new drag event API, or a second drag implementation.

## Design

### 1. NodeView template

NodeCraft.Flow/Themes/Flow.xaml keeps the current three-column node layout:

- column 0: input sockets on the left;
- column 1: stretchable node content;
- column 2: output sockets on the right.

The existing outer grid gains a title row before the socket/content row. A Border named NodeHeader occupies row 0, starts at column 0, and spans all three columns. It uses existing DynamicResource keys:

- colorSubtleBackground for the bar background;
- colorNeutralStroke1 for a subtle bottom separator;
- colorNeutralForeground1 for the title text.

The header has a small minimum height and padding, Cursor="SizeAll", and a hit-testable surface across the full node width. Its centered, single-line TextBlock named NodeTitle binds to the templated parent NodeModel.Name property.

The existing row 1 socket panels and InnerNode keep their columns, top alignment, stretch bindings, and margins. The resize thumb remains in the final row.

### 2. Drag behavior

The header does not handle mouse events itself. FlowCanvas.Canvas_PreviewMouseDown already finds the ancestor NodeView, ignores only interactive editor controls, and enters PreDragMode when the click is not on a connector. The header is a non-editor visual with no connector, so a left drag from any point in NodeHeader follows the existing node movement path, including selection and multi-selection behavior.

This avoids competing mouse capture or drag state. The move cursor makes the existing behavior discoverable without changing how content editors, sockets, canvas selection, or middle-button panning work.

### 3. Content factory

DefaultFlowNodeContentFactory no longer creates a duplicate node.Name title in the ordinary vertical StackPanel.

BuildImagePreview also removes its internal Image label row. The image preview Grid keeps the image area as a star row and the optional path/error/status content below it. With the title owned by NodeView, the preview receives the full content area and remains fillable when the node is resized.

The factory continues to choose StackPanel for ordinary content and Grid for image previews. Plugin content supplied through ContentFactory is not required to use either root type.

### 4. Compatibility and failure behavior

The title is read from the same NodeModel.Name property currently used by the content factory, so existing graph loading, node registration, localization defaults, and persisted names remain unchanged. No model or serializer changes are required.

If a node name is empty, the title text simply renders empty; no fallback or rename behavior is introduced. Existing image loading errors and placeholders remain in the preview area below the title bar.

## Testing strategy

Use the existing self-running NodeCraft.Tests harness and STA/WPF helpers:

1. Extend the Flow.xaml contract test to assert that NodeHeader spans the three columns, uses the title-bar resource keys, has Cursor="SizeAll", and contains NodeTitle with the NodeModel.Name binding.
2. Add a themed STA test that creates a NodeView with a named NodeModel, applies the template, and verifies the rendered NodeTitle.Text equals the model name.
3. Add a content-factory regression assertion that ordinary content does not duplicate the node name and that image preview rows begin with the star-sized preview area while preserving the optional path/status row.
4. Keep the existing resize, socket, connector, graph, and full-suite tests passing.

Run:

    dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
    dotnet build NodeCraft.sln

## Acceptance criteria

- Every NodeView has a visible top title bar.
- The title bar displays the current NodeModel.Name.
- The title bar spans the full node width and presents a move cursor.
- Dragging from the title bar uses the existing node drag behavior.
- Input sockets remain on the left and output sockets remain on the right.
- Ordinary content and image previews no longer show a duplicate internal node title.
- Image previews retain a star-sized fill region and resize with the node.
- Editor controls, connector interactions, selection, panning, resize, serialization, and plugin content behavior do not regress.
- New regression tests and the existing full test suite pass.
