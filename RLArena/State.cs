namespace RLArena;

internal class State
{
    public const int NUM_ACTIONS_PAST = 5;

    States state;
    Actions action;
    CircularBuffer<Actions> pastActions = new CircularBuffer<Actions>(NUM_ACTIONS_PAST);
}