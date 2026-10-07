// How clients see a cancellation policy (user story V1-3), on the booking page and to the owner.
export const describePolicy = (noticeHours: number) =>
  noticeHours === 0
    ? 'You can cancel or change a booking online until it starts.'
    : `You can cancel or change a booking online up to ${noticeHours} ${noticeHours === 1 ? 'hour' : 'hours'} before it starts.`;
