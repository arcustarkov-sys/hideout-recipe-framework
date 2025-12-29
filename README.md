# Hideout Recipe Framework

A server-side SPT mod that allows users to define new hideout crafting recipes using JSON files.

## Features
- Add new hideout recipes without modifying the database
- Supports inputs, tools, resources, and generator fuel requirements
- Deterministic recipe IDs
- No overrides of vanilla content

## Usage
Place recipe JSON files in the `Recipes/` directory.
Recipes are loaded and injected at server startup.

## Compatibility
- SPT 4.0.x

## License
MIT
