# ADR 001: Review Gates for Risky Mutations

Status: accepted

## Context

Recruiting messages can change deadlines, interview stages, offers, and rejection state.
An incorrect automatic update can hide a real next action or create a false one.

## Decision

Careeroc routes model-backed changes and risky deterministic changes to an explicit review item before canonical state mutation.
The review item retains bounded evidence, confidence, matching context, and the proposed typed change.

## Consequences

The system prevents silent high-stakes writes in the tested paths.
Users pay a review cost, and conservative matching can create false review routes.
The evaluation reports that cost instead of weakening the gate.
